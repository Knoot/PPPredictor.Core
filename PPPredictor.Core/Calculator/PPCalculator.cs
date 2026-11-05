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
                pool.ScoreSync = pool.ScoreSync ?? new ScoreSyncState();
                RecalculateScoreWeightedSum(pool);
                if (pool.ScoreSync.HasCompleteScores || (pool.LsScores != null && pool.LsScores.Count > 0))
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
                    mapPool.ScoreSync.ResetMismatch();
                    Logging.ErrorPrint($"{_leaderboardInfo.LeaderboardName} profile refresh failed; keeping cached profile and session: {ex.Message}");
                    return (mapPool.SessionPlayer, mapPool.CurrentPlayer);
                }
            }
            return CommitPlayer(mapPool, player, doResetSession);
        }

        internal (PPPPlayer, PPPPlayer) CommitPlayer(PPPMapPool mapPool, PPPPlayer player, bool doResetSession)
        {
            lock (mapPool.DataLock)
            {
                mapPool.ProfileRefreshFailed = false;
                mapPool.ScoreSync.ProfileRevision++;
                if (mapPool.CurrentPlayer.Pp != player.Pp || mapPool.CurrentPlayer.Rank != player.Rank
                    || mapPool.CurrentPlayer.CountryRank != player.CountryRank || mapPool.CurrentPlayer.Country != player.Country)
                    mapPool.PlayerRankingsFetchedAtUtc = DateTime.MinValue;
                mapPool.CurrentPlayer = player;
                if (_leaderboardInfo.Leaderboard == Leaderboard.BeatLeader && mapPool.BeatLeaderSessionRebasePending)
                {
                    // A formula migration rebases only BeatLeader after the complete snapshot, not other boards' sessions.
                    if (!mapPool.ScoresRequireRefresh)
                    {
                        mapPool.SessionPlayer = player;
                        mapPool.BeatLeaderSessionRebasePending = false;
                        Logging.InfoPrint("BeatLeader PPv4.2: session baseline rebased to the refreshed profile after full score synchronization.");
                    }
                }
                else if (doResetSession || mapPool.SessionPlayer == null || NeedsResetSession())
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

        internal string GetUserId(PPPMapPool mapPool)
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

        internal async Task<bool> GetPlayerScores(PPPMapPool mapPool, int pageSize, int largePageSize, bool fetchOnePage = false, Action<int, int> progressChanged = null, PPPScoreSetData expectedScore = null, bool forceFullRefresh = false, bool allowFullRefresh = true,
            bool requireAllExpected = false, Action onCommitted = null)
        {
            try
            {
                string userId = GetUserId(mapPool);
                Dictionary<string, ShortScore> cached;
                Dictionary<string, PPPScoreSetData> expected;
                DateTimeOffset lastScoreSet;
                bool hadCache;
                lock (mapPool.DataLock)
                {
                    mapPool.ScoreSync.LastFetchUnconfirmed = false;
                    hadCache = mapPool.HasScoreDataFetched;
                    forceFullRefresh |= mapPool.ScoresRequireRefresh || !hadCache;
                    forceFullRefresh |= !string.IsNullOrEmpty(mapPool.ScoreSync.OwnerId) && mapPool.ScoreSync.OwnerId != userId;
                    if (!allowFullRefresh && (forceFullRefresh || !_leaderboardInfo.HasGetRecentScoresFunctionality)) return false;
                    cached = (mapPool.LsScores ?? new List<ShortScore>()).GroupBy(x => x.Searchstring)
                        .ToDictionary(x => x.Key, x => new ShortScore(x.Key, x.Max(score => score.Pp)));
                    expected = new Dictionary<string, PPPScoreSetData>(mapPool.ScoreSync.ExpectedScores, StringComparer.OrdinalIgnoreCase);
                    lastScoreSet = mapPool.DtLastScoreSet;
                }
                bool useAll = _leaderboardInfo.HasGetAllScoresFunctionality
                    && (!_leaderboardInfo.HasGetRecentScoresFunctionality || cached.Count == 0 || forceFullRefresh);
                bool confirmed = expectedScore == null;
                bool complete = false, more = true;
                int page = 1;
                var fetched = new Dictionary<string, ShortScore>();
                var confirmedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                DateTimeOffset newest = new DateTime(2000, 1, 1);
                while (more)
                {
                    NetworkUtil.RequestCancellation.ThrowIfCancellationRequested();
                    var scores = useAll ? await GetAllScores(userId, mapPool)
                        : await GetRecentScores(userId, forceFullRefresh || cached.Count == 0 ? largePageSize : pageSize, page, mapPool);
                    NetworkUtil.RequestCancellation.ThrowIfCancellationRequested();
                    if (scores == null || scores.IsError) throw new InvalidOperationException("Score fetch failed.");
                    if (!useAll && scores.Page != page) throw new InvalidOperationException("API returned an unexpected scores page.");
                    complete = useAll || scores.Page * scores.ItemsPerPage >= scores.Total;
                    more = !complete;
                    if (!useAll && scores.Total > 0 && scores.ItemsPerPage > 0)
                        progressChanged?.Invoke((int)Math.Min(scores.Page * scores.ItemsPerPage, scores.Total), (int)scores.Total);
                    foreach (PPPScore score in scores.LsPPPScore)
                    {
                        if (double.IsNaN(score.Pp) || double.IsInfinity(score.Pp) || score.Pp < 0)
                            throw new InvalidOperationException("API returned invalid score PP.");
                        string key = CreateSeachString(score.SongHash, score.GameMode, (int)score.Difficulty1);
                        if (fetched.TryGetValue(key, out var duplicate) && duplicate.Pp != score.Pp && !useAll)
                            throw new InvalidOperationException("Score changed between pages; refusing an inconsistent snapshot.");
                        fetched[key] = new ShortScore(key, duplicate == null ? score.Pp : Math.Max(duplicate.Pp, score.Pp));
                        if (MatchesExpected(key, score, expectedScore)) confirmed = true;
                        if (expected.TryGetValue(key, out var pending) && MatchesExpected(key, score, pending)) confirmedKeys.Add(key);
                        // The timestamp stops pagination, not application of authoritative values already returned.
                        if (!forceFullRefresh && !useAll && cached.ContainsKey(key) && lastScoreSet >= score.TimeSet
                            && (expectedScore == null || confirmed)) more = false;
                        if (score.TimeSet > newest) newest = score.TimeSet;
                    }
                    if (!forceFullRefresh && !useAll && hadCache)
                    {
                        if (fetchOnePage && expectedScore == null) more = false;
                        if (expectedScore != null && (confirmed || page >= 3)) more = false;
                    }
                    page++;
                    await Task.Delay(_leaderboardInfo.TaskDelayValue, NetworkUtil.RequestCancellation);
                }
                NetworkUtil.RequestCancellation.ThrowIfCancellationRequested();
                lock (mapPool.DataLock)
                {
                    NetworkUtil.RequestCancellation.ThrowIfCancellationRequested();
                    if (requireAllExpected && mapPool.ScoreSync.ExpectedScores.Any(x => !confirmedKeys.Contains(x.Key)
                        || !expected.TryGetValue(x.Key, out var captured) || !ReferenceEquals(x.Value, captured)))
                    {
                        mapPool.ScoreSync.LastFetchUnconfirmed = true;
                        return false;
                    }
                    if (complete || mapPool.LsScores == null) mapPool.LsScores = new List<ShortScore>();
                    foreach (var score in fetched.Values)
                    {
                        var previous = mapPool.LsScores.Find(x => x.Searchstring == score.Searchstring);
                        if (score.Pp == 0) { if (previous != null) mapPool.LsScores.Remove(previous); }
                        else if (previous == null) mapPool.LsScores.Add(score);
                        else previous.Pp = score.Pp;
                    }
                    RecalculateScoreWeightedSum(mapPool);
                    mapPool.DtLastScoreSet = complete ? newest : (newest > lastScoreSet ? newest : lastScoreSet);
                    mapPool.HasScoreDataFetched = hadCache || complete;
                    mapPool.ScoreRefreshFailed = !confirmed;
                    mapPool.ScoreSync.HasCompleteScores |= complete;
                    mapPool.ScoreSync.OwnerId = userId;
                    mapPool.ScoreSync.LastFetchWasComplete = complete;
                    mapPool.ScoreSync.ScoreRevision++;
                    if (complete) mapPool.ScoresRequireRefresh = false;
                    foreach (var key in confirmedKeys)
                        if (mapPool.ScoreSync.ExpectedScores.TryGetValue(key, out var current) && ReferenceEquals(current, expected[key]))
                            mapPool.ScoreSync.ExpectedScores.Remove(key);
                    OnScoresCommitted(mapPool);
                    onCommitted?.Invoke();
                }
                return confirmed;
            }
            catch (OperationCanceledException) when (NetworkUtil.RequestCancellation.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                lock (mapPool.DataLock) { mapPool.ScoreRefreshFailed = true; mapPool.ScoreSync.ResetMismatch(); }
                Logging.ErrorPrint($"PPPredictor getPlayerScores Error; keeping cached scores: {ex.Message}");
                return false;
            }
        }

        private static bool MatchesExpected(string key, PPPScore score, PPPScoreSetData expected) => expected != null
            && string.Equals(key, expected.hash, StringComparison.OrdinalIgnoreCase)
            && (!expected.pp.HasValue || score.Pp >= expected.pp.Value - 0.001)
            && (!expected.score.HasValue || score.ScoreValue >= expected.score.Value);

        internal virtual double? GetComparableTotal(PPPMapPool pool)
        {
            bool comparable = _leaderboardInfo.Leaderboard == Leaderboard.ScoreSaber
                || (_leaderboardInfo.Leaderboard == Leaderboard.BeatLeader && pool.MapPoolType == MapPoolType.Default
                    && pool.LeaderboardContext == LeaderboardContext.BeatLeaderDefault && pool.BeatLeaderPPVersion == 4
                    && pool.BeatLeaderFormulaRevision == DataType.Curve.BeatLeaderV4Curve.FormulaRevision);
            return comparable ? (double?)(pool.LsScores.LastOrDefault()?.WeightedSum ?? 0) : null;
        }

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

                        double ppGainWeighted = Math.Max(0, ppAfterPlay - ppBeforePlay);
                        double ppTotal = currentTotalPP + ppGainWeighted;
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
