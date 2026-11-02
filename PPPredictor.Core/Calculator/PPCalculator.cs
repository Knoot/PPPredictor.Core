using PPPredictor.Core.DataType;
using PPPredictor.Core.DataType.LeaderBoard;
using PPPredictor.Core.DataType.MapPool;
using PPPredictor.Core.DataType.Score;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static PPPredictor.Core.DataType.Enums;

namespace PPPredictor.Core.Calculator
{
    internal abstract class PPCalculator
    {
        private const double SCORETHRESHOLD = 0.0000000001;
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
        internal PPPLeaderboardInfo _leaderboardInfo;
        protected Settings _settings;
        internal Dictionary<string, PPPMapPool> _dctMapPool = new Dictionary<string, PPPMapPool>();

        public SemaphoreSlim Semaphore => semaphore;
        internal virtual void Dispose() { semaphore.Dispose(); }
        internal Func<DateTime> RankCacheUtcNow { get; set; } = () => DateTime.UtcNow;

        public Settings Settings { set => _settings = value; }

        public event EventHandler OnMapPoolRefreshed;

        public PPCalculator(Dictionary<string, PPPMapPool> dctMapPool, Settings settings, Leaderboard leaderboard)
        {
            if (dctMapPool != null)
            {
                _dctMapPool = dctMapPool;
            }
            foreach (var pool in _dctMapPool.Values)
            {
                RecalculateScoreWeightedSum(pool);
                if (pool.LsScores != null && pool.LsScores.Count > 0)
                {
                    pool.HasScoreDataFetched = true;
                }
            }
            _leaderboardInfo = new PPPLeaderboardInfo(leaderboard);
            _settings = settings;
        }

        internal void RecalculateScoreWeightedSum(PPPMapPool mapPool)
        {
            lock (mapPool.DataLock)
            {
                mapPool.LsScores = mapPool.LsScores; //Trigger sorting in setter
                mapPool.DctScorePositionLookup.Clear();
                if (mapPool.LsScores != null)
                {
                    double weightedSum = 0;
                    for (int i = 0; i < mapPool.LsScores.Count; i++)
                    {
                        ShortScore score = mapPool.LsScores[i];
                        weightedSum += WeightPP(score.Pp, i + 1, mapPool);
                        score.WeightedSum = weightedSum;
                        mapPool.DctScorePositionLookup[score.Searchstring] = i;
                    }
                }
            }
        }

        internal PPPMapPool GetMapPoolById(string mapPoolId)
        {
            if (_dctMapPool.TryGetValue(mapPoolId, out var mapPool))
            {
                return mapPool;
            }
            else
            {
                throw new Exception($"MapPool {mapPoolId} for leaderboard {_leaderboardInfo.LeaderboardName} not found");
            }
        }

        internal async Task<(PPPPlayer, PPPPlayer)> UpdatePlayer(PPPMapPool mapPool, bool doResetSession)
        {
            PPPPlayer player;
            try
            {
                player = await GetProfile(mapPool);
                NetworkUtil.RequestCancellation.ThrowIfCancellationRequested();
                if (player == null || player.IsErrorUser || double.IsNaN(player.Pp) || double.IsInfinity(player.Pp) || player.Pp < 0)
                    throw new InvalidOperationException("Profile fetch did not return a valid player.");
            }
            catch (OperationCanceledException) when (NetworkUtil.RequestCancellation.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                lock (mapPool.DataLock)
                {
                    mapPool.ProfileRefreshFailed = true;
                    Logging.ErrorPrint($"{_leaderboardInfo.LeaderboardName} profile refresh failed; keeping cached profile and session: {ex.Message}");
                    return (mapPool.SessionPlayer, mapPool.CurrentPlayer);
                }
            }
            lock (mapPool.DataLock)
            {
                mapPool.ProfileRefreshFailed = false;
                if (mapPool.CurrentPlayer.Pp != player.Pp || mapPool.CurrentPlayer.Rank != player.Rank
                    || mapPool.CurrentPlayer.CountryRank != player.CountryRank || mapPool.CurrentPlayer.Country != player.Country)
                    mapPool.PlayerRankingsFetchedAtUtc = DateTime.MinValue;
                mapPool.CurrentPlayer = player;
                if (doResetSession || mapPool.SessionPlayer == null || NeedsResetSession())
                {
                    _settings.LastSessionReset = DateTime.Now;
                    mapPool.SessionPlayer = player;
                }
                return (mapPool.SessionPlayer, mapPool.CurrentPlayer);
            }
        }

        private bool NeedsResetSession()
        {
            return
                _settings.ResetSessionHours > 0
                && ((DateTime.Now - _settings.LastSessionReset).TotalHours > _settings.ResetSessionHours
                || (DateTime.Now - _settings.LastSessionReset).TotalMinutes < 1); //Parallel reset of multiple scoreboards
        }

        internal async Task<PPPPlayer> GetProfile(PPPMapPool mapPool)
        {
            if (long.TryParse(GetUserId(mapPool), out long longUserId))
            {
                var player = await GetPlayerInfo(longUserId, mapPool);
                return player;
            }
            return new PPPPlayer(true);
        }

        private string GetUserId(PPPMapPool mapPool)
        {
            if (!string.IsNullOrEmpty(mapPool.CustomLeaderboardUserId))
            {
                return mapPool.CustomLeaderboardUserId;
            }
            else
            {
                return _settings.UserId;
            }
        }

        internal async Task<bool> GetPlayerScores(PPPMapPool mapPool, int pageSize, int largePageSize, bool fetchOnePage = false, Action<int, int> progressChanged = null, PPPScoreSetData expectedScore = null, bool forceFullRefresh = false)
        {
            try
            {
                string userId = GetUserId(mapPool);
                Dictionary<string, ShortScore> cachedScores;
                DateTimeOffset lastScoreSet;
                bool hasCompleteCache;
                lock (mapPool.DataLock)
                {
                    forceFullRefresh |= mapPool.ScoresRequireRefresh;
                    cachedScores = (mapPool.LsScores ?? new List<ShortScore>()).GroupBy(x => x.Searchstring)
                        .ToDictionary(x => x.Key, x => new ShortScore(x.Key, x.Max(s => s.Pp)));
                    lastScoreSet = mapPool.DtLastScoreSet;
                    hasCompleteCache = mapPool.HasScoreDataFetched && !forceFullRefresh;
                }
                bool hasNoScores = cachedScores.Count == 0 || forceFullRefresh;
                bool expectedScoreFound = expectedScore == null;
                bool fetchedAllScores = false;
                bool hasMoreData = true;
                int page = 1;
                List<ShortScore> lsNewScores = new List<ShortScore>();
                DateTimeOffset dtNewLastScoreSet = new DateTime(2000, 1, 1);
                while (hasMoreData)
                {
                    NetworkUtil.RequestCancellation.ThrowIfCancellationRequested();
                    PPPScoreCollection playerscores = null;
                    if (_leaderboardInfo.HasGetAllScoresFunctionality && (!_leaderboardInfo.HasGetRecentScoresFunctionality || hasNoScores))
                    {
                        playerscores = await GetAllScores(userId, mapPool);
                        NetworkUtil.RequestCancellation.ThrowIfCancellationRequested();
                        if (playerscores == null || playerscores.IsError) throw new InvalidOperationException("Score fetch failed.");
                        hasMoreData = false;
                    }
                    else
                    {
                        playerscores = await GetRecentScores(userId, hasNoScores ? largePageSize : pageSize, page, mapPool);
                        NetworkUtil.RequestCancellation.ThrowIfCancellationRequested();
                        if (playerscores == null || playerscores.IsError) throw new InvalidOperationException("Score page fetch failed.");
                        if (playerscores.Page != page) throw new InvalidOperationException("API returned an unexpected scores page.");
                        if (playerscores.Total > 0 && playerscores.ItemsPerPage > 0)
                        {
                            int loadedScores = (int)Math.Min(playerscores.Page * playerscores.ItemsPerPage, playerscores.Total);
                            progressChanged?.Invoke(loadedScores, (int)playerscores.Total);
                        }
                        if (playerscores.Page * playerscores.ItemsPerPage >= playerscores.Total || (fetchOnePage && hasCompleteCache && expectedScore == null))
                        {
                            hasMoreData = false;
                        }
                    }
                    NetworkUtil.RequestCancellation.ThrowIfCancellationRequested();
                    fetchedAllScores = forceFullRefresh || (_leaderboardInfo.HasGetAllScoresFunctionality && (!_leaderboardInfo.HasGetRecentScoresFunctionality || hasNoScores));
                    foreach (PPPScore scores in playerscores.LsPPPScore)
                    {
                        string searchString = CreateSeachString(scores.SongHash, scores.GameMode, (int)scores.Difficulty1);
                        cachedScores.TryGetValue(searchString, out ShortScore previousScore);
                        if (expectedScore != null && string.Equals(searchString, expectedScore.hash, StringComparison.OrdinalIgnoreCase)
                            && (!expectedScore.pp.HasValue || scores.Pp >= expectedScore.pp.Value - 0.001)
                            && (!expectedScore.score.HasValue || scores.ScoreValue >= expectedScore.score.Value))
                            expectedScoreFound = true;
                        ShortScore newScore = new ShortScore(searchString, scores.Pp);
                        if (newScore.Pp > 0) // Do not cache unranked scores
                        {
                            if (previousScore == null)
                            {
                                lsNewScores.Add(newScore);
                            }
                            else
                            {
                                if (!fetchedAllScores && lastScoreSet >= scores.TimeSet && (expectedScore == null || expectedScoreFound))
                                {
                                    hasMoreData = false;
                                }
                                else if (fetchedAllScores || newScore.Pp > previousScore.Pp)
                                {
                                    lsNewScores.Add(newScore);
                                }
                            }
                        }
                        dtNewLastScoreSet = scores.TimeSet > dtNewLastScoreSet ? scores.TimeSet : dtNewLastScoreSet;
                    }
                    if (expectedScore != null && hasCompleteCache && expectedScoreFound) hasMoreData = false;
                    if (expectedScore != null && hasCompleteCache && page >= 3) hasMoreData = false;
                    page++;
                    await Task.Delay(_leaderboardInfo.TaskDelayValue, NetworkUtil.RequestCancellation);
                }
                NetworkUtil.RequestCancellation.ThrowIfCancellationRequested();
                // Publish scores, lookup indices and weighted sums together; never hold this lock during HTTP requests.
                lock (mapPool.DataLock)
                {
                    if (mapPool.LsScores == null || forceFullRefresh || (fetchedAllScores && ReplaceScoresOnFullFetch(mapPool)))
                        mapPool.LsScores = new List<ShortScore>();
                    foreach (ShortScore newScore in lsNewScores
                        .GroupBy(x => x.Searchstring)
                        .Select(x => x.OrderByDescending(score => score.Pp).First()))
                    {
                        ShortScore previousScore = mapPool.LsScores.Find(x => x.Searchstring == newScore.Searchstring);
                        if (previousScore == null)
                        {
                            mapPool.LsScores.Add(newScore);
                        }
                        else if (newScore.Pp > previousScore.Pp)
                        {
                            previousScore.Pp = newScore.Pp;
                        }
                    }
                    ;
                    RecalculateScoreWeightedSum(mapPool);
                    mapPool.DtLastScoreSet = dtNewLastScoreSet > mapPool.DtLastScoreSet ? dtNewLastScoreSet : mapPool.DtLastScoreSet;
                    mapPool.HasScoreDataFetched = true;
                    mapPool.ScoreRefreshFailed = !expectedScoreFound;
                    if (fetchedAllScores) mapPool.ScoresRequireRefresh = false;
                    OnScoresCommitted(mapPool);
                }
                return expectedScoreFound;
            }
            catch (OperationCanceledException) when (NetworkUtil.RequestCancellation.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                lock (mapPool.DataLock) mapPool.ScoreRefreshFailed = true;
                Logging.ErrorPrint($"PPPredictor getPlayerScores Error; keeping cached scores: {ex.Message}");
                return false;
            }
        }

        protected virtual bool ReplaceScoresOnFullFetch(PPPMapPool mapPool) => false;
        protected virtual void OnScoresCommitted(PPPMapPool mapPool) { }

        protected void MarkRatingChange(string searchString, PPPStarRating previous, PPPStarRating current, bool categoryChanged = false)
        {
            if (previous == null || (previous.HasSameRatings(current) && !categoryChanged)) return;
            foreach (var pool in _dctMapPool.Values)
            {
                lock (pool.DataLock)
                    if (pool.LsScores?.Any(x => x.Searchstring == searchString) == true) pool.ScoresRequireRefresh = true;
            }
        }

        internal virtual PPGainResult GetPlayerScorePPGain(string mapSearchString, double pp, PPPMapPool mapPool)
        {
            lock (mapPool.DataLock)
            {
                if (mapPool.ScoresRequireRefresh) return new PPGainResult(mapPool.CurrentPlayer.Pp, 0, 0, _settings.PpGainCalculationType);
                return GetPlayerScorePPGainInternal(mapPool.LsScores, mapSearchString, pp, mapPool.CurrentPlayer.Pp, mapPool);
            }
        }

        internal virtual bool HasScoreDataForPPGain(PPPMapPool mapPool)
        {
            return mapPool != null && !mapPool.ScoresRequireRefresh && (mapPool.HasScoreDataFetched || (mapPool.LsScores != null && mapPool.LsScores.Count > 0));
        }

        internal PPGainResult GetPlayerScorePPGainInternal(List<ShortScore> lsScores, string mapSearchString, double pp, double currentTotalPP, PPPMapPool mapPool)
        {
            try
            {
                if (lsScores.Count > 0 && !string.IsNullOrEmpty(mapSearchString))
                {
                    ShortScore oldScore = null;
                    int oldIndex = -1;
                    if (mapPool.DctScorePositionLookup.TryGetValue(mapSearchString, out oldIndex))
                        if (oldIndex >= 0)
                        {
                            oldScore = lsScores[oldIndex];
                        }

                    if (pp > 0)
                    {
                        double ppAfterPlay = 0;
                        double ppBeforePlay = lsScores[lsScores.Count - 1].WeightedSum;
                        double previousPP = oldScore?.Pp ?? 0;

                        if (oldScore != null && pp <= oldScore.Pp)
                        {
                            return new PPGainResult(currentTotalPP, 0, pp - previousPP, _settings.PpGainCalculationType);
                        }

                        int insertionIndex = lsScores.BinarySearch(
                            new ShortScore("", pp),
                            Comparer<ShortScore>.Create((a, b) => b.Pp.CompareTo(a.Pp))
                        );
                        if (insertionIndex < 0)
                        {
                            insertionIndex = ~insertionIndex;
                        }

                        if (insertionIndex > 0)
                        {
                            ppAfterPlay = lsScores[insertionIndex - 1].WeightedSum;
                        }
                        ppAfterPlay += WeightPP(pp, insertionIndex + 1, mapPool);

                        int weightIndexAdd = 2;

                        for (int i = insertionIndex; i < lsScores.Count; i++)
                        {
                            ShortScore score = lsScores[i];
                            if (score.Searchstring == mapSearchString)
                            {
                                weightIndexAdd--;
                                continue;
                            }
                            double weightedScore = WeightPP(score.Pp, i + weightIndexAdd, mapPool);
                            if (weightedScore < SCORETHRESHOLD)
                            {
                                break;
                            }
                            ppAfterPlay += weightedScore;
                        }

                        double ppGainWeighted = Zeroizer(Math.Round(ppAfterPlay - ppBeforePlay, 2, MidpointRounding.AwayFromZero), 0.02);
                        double ppTotal = Math.Round(currentTotalPP + ppGainWeighted, 2, MidpointRounding.AwayFromZero);
                        return new PPGainResult(ppTotal, ppGainWeighted, pp - previousPP, _settings.PpGainCalculationType);
                    }
                    return new PPGainResult(currentTotalPP, pp, oldScore != null ? -oldScore.Pp : 0, _settings.PpGainCalculationType);
                }
                else if (currentTotalPP == 0 && mapPool.HasScoreDataFetched) //If you have not set a score yet, total is = the new pp play
                {
                    return new PPGainResult(pp, pp, pp, _settings.PpGainCalculationType);
                }
                else
                {
                    return new PPGainResult(currentTotalPP, 0, 0, _settings.PpGainCalculationType);
                }
            }
            catch (Exception ex)
            {
                Logging.ErrorPrint($"PPPredictor {_leaderboardInfo?.LeaderboardName} GetPlayerScorePPGain Error: {ex.Message}");
            }
            return currentTotalPP == 0 && mapPool.HasScoreDataFetched
                ? new PPGainResult(pp, pp, pp, _settings.PpGainCalculationType)
                : new PPGainResult(currentTotalPP, 0, 0, _settings.PpGainCalculationType);
        }

        internal async Task<RankGainResult> GetPlayerRankGain(double pp, PPPMapPool mapPool)
        {
            DateTime now = RankCacheUtcNow();
            try
            {
                PPPPlayer player;
                List<PPPPlayer> rankings;
                DateTime fetchedAt;
                lock (mapPool.DataLock)
                {
                    player = mapPool.CurrentPlayer;
                    fetchedAt = mapPool.PlayerRankingsFetchedAtUtc;
                    var age = now - fetchedAt;
                    rankings = age >= TimeSpan.Zero && age < _settings.PlayerRankingCacheDuration
                        ? new List<PPPPlayer>(mapPool.LsPlayerRankings) : new List<PPPPlayer>();
                    if (pp > player.Pp && now < mapPool.PlayerRankingsRetryAfterUtc) return new RankGainResult();
                }
                if (_leaderboardInfo.LeaderboardName == Leaderboard.NoLeaderboard.ToString())
                    return new RankGainResult(0, 0, player);
                if (pp == player.Pp || pp < player.Pp) return new RankGainResult(player.Rank, player.CountryRank, player);
                if (_leaderboardInfo.PlayerPerPages <= 0 || player.Rank <= 0) return new RankGainResult();
                // Refetch when the current profile falls outside the cached range.
                double worstRankFetched = rankings.Select(x => x.Rank).DefaultIfEmpty(Double.MaxValue).Max();
                if (!_leaderboardInfo.HasPPToRankFunctionality && player.Rank > worstRankFetched) rankings = new List<PPPPlayer>();

                double bestRankFetched = rankings.Select(x => x.Rank).DefaultIfEmpty(-1).Min();
                double fetchIndexPage = bestRankFetched > 0 ? Math.Floor((bestRankFetched - 1) / _leaderboardInfo.PlayerPerPages) + _leaderboardInfo.LeaderboardFirstPageIndex : Math.Floor((player.Rank - 1) / _leaderboardInfo.PlayerPerPages) + _leaderboardInfo.LeaderboardFirstPageIndex;
                bool needMoreData = true;
                int pageFetchCount = 0;
                while (needMoreData && pageFetchCount < _leaderboardInfo.PageFetchLimit)
                {
                    NetworkUtil.RequestCancellation.ThrowIfCancellationRequested();
                    int indexOfBetterPlayer = rankings.FindLastIndex(x => x.Pp > pp);
                    //Check if the rank before is actually the rank before, gaps can be created when using PPToRank
                    bool isNextRankActuallyNext = indexOfBetterPlayer > -1 && rankings.Count - 1 > indexOfBetterPlayer &&
                                                        rankings[indexOfBetterPlayer].Rank + 1 == rankings[indexOfBetterPlayer + 1].Rank;
                    if ((_leaderboardInfo.HasPPToRankFunctionality && ((isNextRankActuallyNext && indexOfBetterPlayer != -1) || (bestRankFetched == 1 && indexOfBetterPlayer == -1)))
                        || (!_leaderboardInfo.HasPPToRankFunctionality && (indexOfBetterPlayer != -1 || fetchIndexPage == _leaderboardInfo.LeaderboardFirstPageIndex - 1 || (bestRankFetched == 1 && indexOfBetterPlayer == -1)))) // Includes the first-place case.
                    {
                        //Found a better player or already fetched until rank 1
                        needMoreData = false;
                        continue;
                    }
                    else
                    {
                        bool isLastRankOnPage = false;
                        if (_leaderboardInfo.HasPPToRankFunctionality)
                        {
                            int newRank = await GetPPToRank(mapPool.Id, pp);
                            fetchIndexPage = ((newRank - 1) / _leaderboardInfo.PlayerPerPages);
                            isLastRankOnPage = newRank % _leaderboardInfo.PlayerPerPages == 0;
                        }
                        await FetchPlayerPageAndAddToList(fetchIndexPage, mapPool, rankings);
                        indexOfBetterPlayer = rankings.FindIndex(x => x.Pp > pp);
                        if (_leaderboardInfo.HasPPToRankFunctionality && indexOfBetterPlayer == -1 && fetchIndexPage > _leaderboardInfo.LeaderboardFirstPageIndex)
                        {
                            //Fetch one more page, when it is the first rank on the page, since i check for the index of the better player, otherwise endless loop
                            await FetchPlayerPageAndAddToList(fetchIndexPage - 1, mapPool, rankings);
                        }
                        if (isLastRankOnPage)
                        {
                            await FetchPlayerPageAndAddToList(fetchIndexPage + 1, mapPool, rankings);
                        }
                    }
                    fetchIndexPage--;
                    pageFetchCount++;
                    bestRankFetched = rankings.Select(x => x.Rank).DefaultIfEmpty(-1).Min();
                    await Task.Delay(_leaderboardInfo.TaskDelayValue, NetworkUtil.RequestCancellation);
                }
                NetworkUtil.RequestCancellation.ThrowIfCancellationRequested();
                if (pageFetchCount > 0)
                {
                    lock (mapPool.DataLock)
                    {
                        mapPool.LsPlayerRankings = rankings;
                        mapPool.PlayerRankingsFetchedAtUtc = fetchedAt != DateTime.MinValue
                            && now >= fetchedAt && now - fetchedAt < _settings.PlayerRankingCacheDuration ? fetchedAt : now;
                        mapPool.PlayerRankingsRetryAfterUtc = DateTime.MinValue;
                    }
                }
                double rankAfterPlay = rankings.Where(x => Math.Round(x.Pp, 2, MidpointRounding.AwayFromZero) <= pp).Select(x => x.Rank).DefaultIfEmpty(-1).Min();
                double rankCountryAfterPlay = rankings.Where(x => Math.Round(x.Pp, 2, MidpointRounding.AwayFromZero) <= pp && x.Country == player.Country).Select(x => x.CountryRank).DefaultIfEmpty(-1).Min();
                return new RankGainResult(rankAfterPlay, rankCountryAfterPlay, player, pageFetchCount >= _leaderboardInfo.PageFetchLimit);
            }
            catch (OperationCanceledException) when (NetworkUtil.RequestCancellation.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                lock (mapPool.DataLock) mapPool.PlayerRankingsRetryAfterUtc = now.AddSeconds(15);
                Logging.ErrorPrint($"PPPredictor GetPlayerRankGain Error: {ex.Message}");
                return new RankGainResult();
            }
        }

        private async Task FetchPlayerPageAndAddToList(double fetchIndexPage, PPPMapPool mapPool, List<PPPPlayer> rankings)
        {
            List<PPPPlayer> players = await GetPlayers(fetchIndexPage, mapPool);
            NetworkUtil.RequestCancellation.ThrowIfCancellationRequested();
            if (players == null || players.Count == 0 || players.Any(x => x == null || x.IsErrorUser
                || x.Rank <= 0 || double.IsNaN(x.Rank) || double.IsInfinity(x.Rank)
                || x.Pp < 0 || double.IsNaN(x.Pp) || double.IsInfinity(x.Pp)))
                throw new InvalidOperationException("Player rankings fetch returned no valid range.");
            var ranks = new HashSet<double>(players.Select(x => x.Rank));
            rankings.RemoveAll(x => ranks.Contains(x.Rank));
            rankings.AddRange(players.GroupBy(x => x.Rank).Select(x => x.Last()));
            rankings.Sort((left, right) => left.Rank.CompareTo(right.Rank));
        }

        internal double? GetPersonalBest(string mapSearchString, PPPMapPool mapPool)
        {
            ShortScore score = mapPool.LsScores.Find(x => x.Searchstring == mapSearchString);
            if (score != null)
            {
                return score.Pp;
            }
            return null;
        }

        internal virtual Task<int> GetPPToRank(string mappoolid, double pp)
        {
            return Task.FromResult(0);
        }

        internal double CalculatePPatPercentage(PPPBeatMapInfo _currentBeatMapInfo, PPPMapPool mapPool, double percentage, bool failed, bool paused)
        {
            return mapPool.Curve.CalculatePPatPercentage(_currentBeatMapInfo, percentage, failed, paused, mapPool.LeaderboardContext);
        }

        internal double CalculateMaxPP(PPPBeatMapInfo _currentBeatMapInfo, PPPMapPool mapPool)
        {
            return mapPool.Curve.CalculateMaxPP(_currentBeatMapInfo, mapPool.LeaderboardContext);
        }

        internal double WeightPP(double rawPP, int index, PPPMapPool mapPool)
        {
            return rawPP * GetWeightMulitplier(index, mapPool);
        }

        private double GetWeightMulitplier(int index, PPPMapPool mapPool)
        {
            if (mapPool.DctWeightLookup.TryGetValue(index, out double value))
            {
                return value;
            }
            double mult = CalculateWeightMulitplier(index, mapPool.AccumulationConstant);
            mapPool.DctWeightLookup[index] = mult;
            return mult;
        }

        protected virtual double CalculateWeightMulitplier(int index, float accumulationConstant)
        {
            return Math.Pow(accumulationConstant, (index - 1));
        }

        public static string CreateSeachString(string hash, string gameMode, int difficulty)
        {
            return $"{hash}_{gameMode}_{difficulty}".ToUpper();
        }

        public static double Zeroizer(double pp, double limit = 0.01)
        {
            if (pp < -limit || pp > limit) return pp;
            if ((pp > -limit && pp < 0) || (pp < limit && pp > 0)) return 0;
            return pp;
        }

        protected void SendMapPoolRefreshed()
        {
            OnMapPoolRefreshed?.Invoke(this, null);
        }

        internal bool IsPlayerFound(PPPMapPool mapPool)
        {
            if (!mapPool.IsPlayerFound)
            {
                return false;
            }
            return true;
        }

        internal async Task UpdateMapPoolDetails(PPPMapPool mapPool)
        {
            if (mapPool.DtUtcLastRefresh < DateTime.UtcNow.AddDays(-1))
            {
                await InternalUpdateMapPoolDetails(mapPool);
                mapPool.DtUtcLastRefresh = DateTime.UtcNow;
            }
        }

        internal abstract List<PPPMapPoolShort> GetMapPools();

        internal abstract Task<PPPPlayer> GetPlayerInfo(long userId, PPPMapPool mapPool);

        internal abstract Task<PPPScoreCollection> GetRecentScores(string userId, int pageSize, int page, PPPMapPool mapPool);

        internal abstract Task<PPPScoreCollection> GetAllScores(string userId, PPPMapPool mapPool);

        internal abstract Task<List<PPPPlayer>> GetPlayers(double fetchIndexPage, PPPMapPool mapPool);

        internal abstract Task<PPPBeatMapInfo> GetBeatMapInfoAsync(PPPBeatMapInfo beatMapInfo, PPPMapPool mapPool);

        internal abstract PPPBeatMapInfo ApplyModifiersToBeatmapInfo(PPPBeatMapInfo beatMapInfo, PPPMapPool mapPool, DataType.BeatSaberEncapsulation.GameplayModifiers gameplayModifiers, bool levelFailed = false, bool levelPaused = false);

        public abstract string CreateSeachString(string hash, DataType.BeatSaberEncapsulation.BeatmapKey beatmapKey);

        internal abstract Task InternalUpdateMapPoolDetails(PPPMapPool mapPool);

        public abstract Task UpdateAvailableMapPools();
        internal abstract bool IsScoreSetOnCurrentMapPool(PPPMapPool mapPool, PPPScoreSetData score);
        internal virtual string GetStarDisplay(PPPBeatMapInfo beatMapInfo)
        {
            return FormatStarRating(beatMapInfo.ModifiedStarRating?.Stars);
        }
        internal string FormatStarRating(double? starRating)
        {
            if ((starRating ?? 0) < 0)
            {
                return $"{0:N2}★";
            }
            else
            {
                return $"{starRating ?? 0:N2}★";
            }
        }

        internal virtual PPPMapPoolShort FindPoolWithPlayListId(string playListId)
        {
            return null;
        }

        internal virtual PPPMapPoolShort FindMapPoolForBeatMapInfo(PPPBeatMapInfo beatMapInfo)
        {
            return null;
        }

        internal virtual Task<PPPMapPoolShort> FindMapPoolForBeatMapInfoAsync(PPPBeatMapInfo beatMapInfo)
        {
            return Task.FromResult(FindMapPoolForBeatMapInfo(beatMapInfo));
        }

        internal PPPMapPoolShort FindPoolWithSyncURL(string syncUrl)
        {
            return _dctMapPool.Values.FirstOrDefault(x => x.SyncUrl == syncUrl);
        }

    }
}
