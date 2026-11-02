using PPPredictor.Core.DataType;
using PPPredictor.Core.DataType.Curve;
using PPPredictor.Core.DataType.MapPool;
using PPPredictor.Core.DataType.Score;
using PPPredictor.Core.Interface;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static PPPredictor.Core.DataType.Enums;
using static PPPredictor.Core.DataType.LeaderBoard.AccSaberDataTypes;

namespace PPPredictor.Core.Calculator
{
    class PPCalculatorAccSaber<ASAPI> : PPCalculator where ASAPI : IAccSaberAPI, new ()
    {
        private readonly ASAPI accsaberapi;
        private readonly string unsetPoolId = "-1";
        private Dictionary<string, PPPMapPool> categoryScores = new Dictionary<string, PPPMapPool>();
        private Dictionary<string, PPPMapPool> pendingOverallScores;
        private bool overallScoresLoaded;
        private readonly TimedAsyncCache<AccSaberMap> mapCache;
        private readonly Func<DateTime> utcNow;
        private readonly HashSet<string> failedMapLookups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private (string Path, DateTime Write, long Length)? loadedFile, failedFile;
        private DateTime fileRetryAfterUtc;

        public PPCalculatorAccSaber(Dictionary<string, PPPMapPool> dctMapPool, Settings settings) : this(dctMapPool, settings, new ASAPI())
        {
        }

        internal PPCalculatorAccSaber(Dictionary<string, PPPMapPool> dctMapPool, Settings settings, ASAPI api, Func<DateTime> utcNow = null) : base(dctMapPool, settings, Leaderboard.AccSaber)
        {
            accsaberapi = api;
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
            mapCache = new TimedAsyncCache<AccSaberMap>(settings?.MapInfoCacheDuration ?? TimeSpan.FromMinutes(5), this.utcNow);
        }
        internal override async Task<PPPBeatMapInfo> GetBeatMapInfoAsync(PPPBeatMapInfo beatMapInfo, PPPMapPool mapPool)
        {
            try
            {
                if (!string.IsNullOrEmpty(beatMapInfo.CustomLevelHash))
                {
                    LoadAccSaberMapCache();
                    if (!HasFreshMapInfo(beatMapInfo)) await LoadAccSaberMapByHash(beatMapInfo.CustomLevelHash);
                    string searchString = CreateSeachString(beatMapInfo.CustomLevelHash, beatMapInfo.BeatmapKey);
                    ShortScore cachedInfo;
                    lock (mapPool.DataLock) cachedInfo = mapPool.LsLeaderboadInfo.FirstOrDefault(x => x.Searchstring == searchString);
                    if(cachedInfo != null)
                    {
                        return new PPPBeatMapInfo(beatMapInfo, new PPPStarRating(cachedInfo.StarRating.Stars))
                        { MapInfoRefreshFailed = failedMapLookups.Contains(beatMapInfo.CustomLevelHash) };
                    }
                    return new PPPBeatMapInfo(beatMapInfo, new PPPStarRating(0))
                    { MapInfoRefreshFailed = failedMapLookups.Contains(beatMapInfo.CustomLevelHash) };
                }
                return new PPPBeatMapInfo(beatMapInfo, new PPPStarRating(0));
            }
            catch (Exception ex)
            {
                Logging.ErrorPrint($"PPCalculatorAccSaber GetBeatMapInfoAsync Error: {ex.Message}");
                return new PPPBeatMapInfo(beatMapInfo, new PPPStarRating(-1)) { MapInfoRefreshFailed = true };
            }
        }

        internal override async Task<PPPPlayer> GetPlayerInfo(long userId, PPPMapPool mapPool)
        {
            try
            {
                if (mapPool.Id == unsetPoolId) return new PPPPlayer(true);
                AccSaberPlayer player = await accsaberapi.GetAccSaberUserByPool(userId, mapPool.Id);
                mapPool.IsPlayerFound = true;
                return new PPPPlayer(player);
            }
            catch (Exception ex)
            {
                Logging.ErrorPrint($"PPCalculatorAccSaber GetPlayerInfo Error: {ex.Message}");
                return new PPPPlayer(true);
            }
        }

        internal override async Task<List<PPPPlayer>> GetPlayers(double fetchIndexPage, PPPMapPool mapPool)
        {
            if(!IsPlayerFound(mapPool)) return new List<PPPPlayer>();
            try
            {
                List<PPPPlayer> lsPlayer = new List<PPPPlayer>();
                if (mapPool.Id == unsetPoolId) return lsPlayer;
                List<AccSaberPlayer> lsAccPlayer = await accsaberapi.GetPlayerListForMapPool(fetchIndexPage, mapPool.Id);
                foreach (AccSaberPlayer accPlayer in lsAccPlayer)
                {
                    lsPlayer.Add(new PPPPlayer(accPlayer));
                }
                return lsPlayer;
            }
            catch (Exception ex)
            {
                Logging.ErrorPrint($"PPCalculatorAccSaber GetPlayers Error: {ex.Message}");
                return new List<PPPPlayer>();
            }
        }

        internal override Task<PPPScoreCollection> GetRecentScores(string userId, int pageSize, int page, PPPMapPool mapPool)
        {
            //No recent scores functionality based on map pool available
            return Task.FromResult(new PPPScoreCollection());
        }

        internal override async Task<PPPScoreCollection> GetAllScores(string userId, PPPMapPool mapPool)
        {
            pendingOverallScores = null;
            if (!IsPlayerFound(mapPool)) return PPPScoreCollection.Failed();
            try
            {
                if (mapPool.Id == unsetPoolId) return new PPPScoreCollection();
                if (mapPool.MapPoolType != MapPoolType.Default)
                {

                    List<AccSaberScores> lsAccSaberScores = await accsaberapi.GetAllScoresByPool(userId, mapPool.Id);
                    return new PPPScoreCollection(lsAccSaberScores, 0);
                }
                else
                {
                    //Special case for overall leaderboard need the scores seperated
                    List<AccSaberScores> lsAccSaberScores = await accsaberapi.GetAllScores(userId);
                    if (lsAccSaberScores.Any(x => double.IsNaN(x.ap) || double.IsInfinity(x.ap) || x.ap < 0))
                        throw new InvalidOperationException("AccSaber returned an invalid AP value.");
                    var newScores = new[] { "standard", "tech", "true" }.ToDictionary(x => x,
                        x => new PPPMapPool(x, MapPoolType.Custom, x, 0, 0, CurveParser.ParseToCurve(new CurveInfo(CurveType.AccSaber))));
                    foreach (var group in lsAccSaberScores.Where(x => x.ap > 0).GroupBy(x => NormalizeCategoryToPoolId(x.categoryDisplayName)))
                    {
                        if (!newScores.TryGetValue(group.Key, out var category))
                            throw new InvalidOperationException("AccSaber score has an unknown category.");
                        foreach (var score in group)
                        {
                            PPPScore pppScore = new PPPScore(score);
                            string searchString = CreateSeachString(pppScore.SongHash, pppScore.GameMode, (int)pppScore.Difficulty1);
                            if (pppScore.Pp > 0) category.LsScores.Add(new ShortScore(searchString, pppScore.Pp));
                        }
                    }
                    foreach (var category in newScores.Values)
                    {
                        category.LsScores = category.LsScores.GroupBy(x => x.Searchstring)
                            .Select(x => new ShortScore(x.Key, x.Max(score => score.Pp))).ToList();
                        RecalculateScoreWeightedSum(category);
                        category.CurrentPlayer = new PPPPlayer { Pp = category.LsScores.LastOrDefault()?.WeightedSum ?? 0 };
                        category.HasScoreDataFetched = true;
                    }
                    var result = new PPPScoreCollection(lsAccSaberScores, 0);
                    pendingOverallScores = newScores;
                    return result;
                }
            }
            catch (Exception ex)
            {
                Logging.ErrorPrint($"PPCalculatorAccSaber GetAllScores Error: {ex.Message}");
                return PPPScoreCollection.Failed();
            }
        }

        protected override bool ReplaceScoresOnFullFetch(PPPMapPool mapPool) => mapPool.Id != unsetPoolId;

        protected override void OnScoresCommitted(PPPMapPool mapPool)
        {
            if (mapPool.MapPoolType == MapPoolType.Default && pendingOverallScores != null)
            {
                categoryScores = pendingOverallScores;
                overallScoresLoaded = true;
                pendingOverallScores = null;
            }
        }

        internal override async Task InternalUpdateMapPoolDetails(PPPMapPool mapPool)
        {
            if (!IsPlayerFound(mapPool)) return;
            try
            {
                if (mapPool.Id == unsetPoolId) return;
                mapPool.LsMapPoolEntries.Clear();
                if (LoadAccSaberMapCache())
                {
                    return;
                }
                List<AccSaberRankedMap> rankedSongs = new List<AccSaberRankedMap>();
                if (mapPool.MapPoolType != MapPoolType.Default)
                {

                    rankedSongs = await this.accsaberapi.GetRankedMaps(mapPool.PlayListId);
                }
                else
                {
                    rankedSongs = await this.accsaberapi.GetAllRankedMaps();
                }
                foreach (AccSaberRankedMap song in rankedSongs)
                {
                    AddLeaderboardInfo(mapPool, song.songHash, song.difficulty, song.complexity, song.categoryDisplayName);
                }
            }
            catch (Exception ex)
            {
                Logging.ErrorPrint($"PPCalculatorAccSaber UpdateMapPoolDetails Error: {ex.Message}");
            }
        }

        internal override PPPBeatMapInfo ApplyModifiersToBeatmapInfo(PPPBeatMapInfo beatMapInfo, PPPMapPool mapPool, DataType.BeatSaberEncapsulation.GameplayModifiers gameplayModifiers, bool levelFailed = false, bool levelPaused = false)
        {
            beatMapInfo.ModifiedStarRating = new PPPStarRating(beatMapInfo.BaseStarRating.Stars);
            return beatMapInfo;
        }

        internal override bool IsScoreSetOnCurrentMapPool(PPPMapPool mapPool, PPPScoreSetData score)
        {
            return mapPool.Id != unsetPoolId && mapPool.LsLeaderboadInfo.Exists(x =>
                string.Equals(x.Searchstring, score.hash, StringComparison.OrdinalIgnoreCase));
        }

        protected override double CalculateWeightMulitplier(int index, float accumulationConstant)
        {
            double y1 = 0.1;
            double x1 = 15;
            double k = 0.4;
            double x0 = -(Math.Log((1 - y1) / (y1 * Math.Exp(k * x1) - 1)) / k);
            return (1 + Math.Exp(-k * x0)) / (1 + Math.Exp(k * (index - 1 - x0)));
        }

        internal override PPGainResult GetPlayerScorePPGain(string mapSearchString, double pp, PPPMapPool mapPool)
        {
            lock (mapPool.DataLock) return GetAccSaberScorePPGain(mapSearchString, pp, mapPool);
        }

        private PPGainResult GetAccSaberScorePPGain(string mapSearchString, double pp, PPPMapPool mapPool)
        {
            if (mapPool.ScoresRequireRefresh) return new PPGainResult(mapPool.CurrentPlayer.Pp, 0, 0, _settings.PpGainCalculationType);
            if(mapPool.MapPoolType != MapPoolType.Default)
            {
                if (!HasCustomPoolScoreDataForPPGain(mapPool))
                {
                    return new PPGainResult(mapPool.CurrentPlayer.Pp, 0, 0, _settings.PpGainCalculationType);
                }

                return GetPlayerScorePPGainInternal(mapPool.LsScores, mapSearchString, pp, mapPool.CurrentPlayer.Pp, mapPool);
            }
            else
            {
                var rankedMapInfo = mapPool.LsLeaderboadInfo.FirstOrDefault(x => x.Searchstring == mapSearchString);
                if (rankedMapInfo != null)
                {
                    string category = NormalizeCategoryToPoolId(rankedMapInfo.Category);
                    if (!overallScoresLoaded || !categoryScores.TryGetValue(category, out var categoryPool))
                    {
                        return new PPGainResult(mapPool.CurrentPlayer.Pp, 0, 0, _settings.PpGainCalculationType);
                    }

                    PPGainResult ppGain = GetPlayerScorePPGainInternal(categoryPool.LsScores, mapSearchString, pp, categoryPool.CurrentPlayer.Pp, categoryPool);
                    return new PPGainResult(Math.Round(mapPool.CurrentPlayer.Pp + ppGain.PpGainWeighted, 2, MidpointRounding.AwayFromZero),
                        ppGain.PpGainWeighted, ppGain.PpGainRaw, _settings.PpGainCalculationType);
                }
                return new PPGainResult(mapPool.CurrentPlayer.Pp, 0, 0, _settings.PpGainCalculationType);
            }
        }

        internal override bool HasScoreDataForPPGain(PPPMapPool mapPool)
        {
            if (mapPool?.MapPoolType == MapPoolType.Default)
            {
                return mapPool.Id != unsetPoolId && !mapPool.ScoresRequireRefresh && overallScoresLoaded;
            }

            return HasCustomPoolScoreDataForPPGain(mapPool);
        }

        private bool HasCustomPoolScoreDataForPPGain(PPPMapPool mapPool)
        {
            return mapPool != null
                && mapPool.CurrentPlayer != null
                && mapPool.Id != unsetPoolId
                && base.HasScoreDataForPPGain(mapPool);
        }

        public override string CreateSeachString(string hash, DataType.BeatSaberEncapsulation.BeatmapKey beatmapKey)
        {
            return $"{hash}_SOLO{beatmapKey.serializedName}_{ParsingUtil.ParseDifficultyNameToInt(beatmapKey.difficulty.ToString())}".ToUpper();
        }

        public override async Task UpdateAvailableMapPools()
        {
            try
            {
                EnsureAccSaberMapPools();

                if (LoadAccSaberMapCache())
                {
                    SendMapPoolRefreshed();
                    return;
                }

                List<AccSaberMapPool> mapPool = await accsaberapi.GetAccSaberMapPools();
                //check if this map pool is already in list
                foreach (AccSaberMapPool newMapPool in mapPool)
                {
                    if (_dctMapPool.TryGetValue(newMapPool.categoryName, out PPPMapPool oldPool))
                    {
                        if (DateTime.UtcNow.AddDays(-1) < oldPool.DtUtcLastRefresh)
                        {
                            continue; //Do not get Playlist if it has been updated less than a day ago.
                        }
                    }
                    else
                    {
                        int sortindex = Array.IndexOf(new object[3] { "standard", "tech", "true" }, newMapPool.categoryName) + 1;
                        oldPool = new PPPMapPool(newMapPool.categoryName, newMapPool.categoryName, MapPoolType.Custom, newMapPool.categoryDisplayName, 0, sortindex, CurveParser.ParseToCurve(new CurveInfo(CurveType.AccSaber)), string.Empty);
                        if (!_dctMapPool.ContainsKey(oldPool.Id)) _dctMapPool.Add(oldPool.Id, oldPool);
                    }
                }
                if(!_dctMapPool.ContainsKey("overall"))
                {
                    var overallMapPool = new PPPMapPool("overall", "overall", MapPoolType.Default, "Overall", 0, 0, CurveParser.ParseToCurve(new CurveInfo(CurveType.AccSaber)), string.Empty);
                    if (!_dctMapPool.ContainsKey(overallMapPool.Id)) _dctMapPool.Add(overallMapPool.Id, overallMapPool);
                }
                if (_dctMapPool.TryGetValue("overall", out PPPMapPool allMapsPool))
                {
                    await InternalUpdateMapPoolDetails(allMapsPool);
                    allMapsPool.DtUtcLastRefresh = DateTime.UtcNow;
                }
                SendMapPoolRefreshed();
            }
            catch (Exception ex)
            {
                Logging.ErrorPrint($"PPCalculatorAccSaber UpdateAvailableMapPools Error: {ex.Message}");
            }
        }

        internal override List<PPPMapPoolShort> GetMapPools()
        {
            return _dctMapPool.Values.OrderBy(x => x.SortIndex).Select(x => (PPPMapPoolShort)x).ToList();
        }

        internal override PPPMapPoolShort FindMapPoolForBeatMapInfo(PPPBeatMapInfo beatMapInfo)
        {
            if (beatMapInfo == null || string.IsNullOrEmpty(beatMapInfo.CustomLevelHash))
            {
                return null;
            }

            LoadAccSaberMapCache();
            string searchString = CreateSeachString(beatMapInfo.CustomLevelHash, beatMapInfo.BeatmapKey);
            ShortScore leaderboardInfo = _dctMapPool.Values
                .SelectMany(x => { lock (x.DataLock) return (x.LsLeaderboadInfo ?? new List<ShortScore>()).ToArray(); })
                .FirstOrDefault(x => x.Searchstring == searchString);
            if (leaderboardInfo == null)
            {
                return null;
            }

            string categoryId = NormalizeCategoryToPoolId(leaderboardInfo.Category);
            if (string.IsNullOrEmpty(categoryId) || !_dctMapPool.TryGetValue(categoryId, out PPPMapPool mapPool))
            {
                return null;
            }

            return mapPool;
        }

        internal override async Task<PPPMapPoolShort> FindMapPoolForBeatMapInfoAsync(PPPBeatMapInfo beatMapInfo)
        {
            PPPMapPoolShort mapPool = FindMapPoolForBeatMapInfo(beatMapInfo);
            if (beatMapInfo == null || string.IsNullOrEmpty(beatMapInfo.CustomLevelHash) || HasFreshMapInfo(beatMapInfo))
            {
                return mapPool;
            }

            await LoadAccSaberMapByHash(beatMapInfo.CustomLevelHash);
            return FindMapPoolForBeatMapInfo(beatMapInfo);
        }

        private bool HasFreshMapInfo(PPPBeatMapInfo map)
        {
            string key = CreateSeachString(map.CustomLevelHash, map.BeatmapKey);
            foreach (var pool in _dctMapPool.Values)
            {
                lock (pool.DataLock)
                {
                    var info = pool.LsLeaderboadInfo.FirstOrDefault(x => x.Searchstring == key);
                    if (info != null)
                    {
                        var age = utcNow() - info.FetchTime.ToUniversalTime();
                        if (age >= TimeSpan.Zero && age < _settings.MapInfoCacheDuration) return true;
                    }
                }
            }
            return false;
        }

        private void EnsureAccSaberMapPools()
        {
            var defaultMapPool = new PPPMapPool(MapPoolType.Default, $"☞ Select a map pool ☜", 0, 0, CurveParser.ParseToCurve(new CurveInfo(CurveType.AccSaber)), 0);
            if (!_dctMapPool.ContainsKey(defaultMapPool.Id)) _dctMapPool.Add(defaultMapPool.Id, defaultMapPool);

            EnsureAccSaberMapPool("standard", "Standard", 1);
            EnsureAccSaberMapPool("tech", "Tech", 2);
            EnsureAccSaberMapPool("true", "True", 3);

            if (!_dctMapPool.ContainsKey("overall"))
            {
                var overallMapPool = new PPPMapPool("overall", "overall", MapPoolType.Default, "Overall", 0, 0, CurveParser.ParseToCurve(new CurveInfo(CurveType.AccSaber)), string.Empty);
                _dctMapPool.Add(overallMapPool.Id, overallMapPool);
            }
        }

        private void EnsureAccSaberMapPool(string id, string name, int sortIndex)
        {
            if (_dctMapPool.ContainsKey(id))
            {
                return;
            }

            _dctMapPool.Add(id, new PPPMapPool(id, id, MapPoolType.Custom, name, 0, sortIndex, CurveParser.ParseToCurve(new CurveInfo(CurveType.AccSaber)), string.Empty));
        }

        private bool LoadAccSaberMapCache()
        {
            if (string.IsNullOrEmpty(_settings.AccSaberMapCachePath) || !File.Exists(_settings.AccSaberMapCachePath)) return false;
            (string Path, DateTime Write, long Length) stamp = default;
            try
            {
                var file = new FileInfo(_settings.AccSaberMapCachePath);
                stamp = (file.FullName, file.LastWriteTimeUtc, file.Length);
                if (loadedFile.HasValue && loadedFile.Value == stamp) return true;
                if (failedFile.HasValue && failedFile.Value == stamp && utcNow() < fileRetryAfterUtc) return loadedFile.HasValue;
                var catalog = JsonConvert.DeserializeObject<AccSaberMapCache>(File.ReadAllText(file.FullName));
                if (catalog?.content == null) throw new InvalidOperationException("AccSaber map cache has no content.");
                EnsureAccSaberMapPools();
                var incoming = _dctMapPool.Values.Where(x => x.Id != unsetPoolId).ToDictionary(x => x.Id, x => new List<ShortScore>());
                foreach (var map in catalog.content)
                {
                    if (map.difficulties == null) throw new InvalidOperationException("AccSaber map cache has invalid difficulties.");
                    foreach (var diff in map.difficulties)
                    {
                        string category = NormalizeCategoryToPoolId(diff.categoryCode);
                        if (!incoming.ContainsKey(category)) throw new InvalidOperationException("AccSaber map cache has an unknown category.");
                        var info = CreateMapInfo(map.songHash, diff.difficulty, diff.complexity, GetCategoryDisplayName(category),
                            diff.characteristic, stamp.Write);
                        incoming[category].Add(info);
                        incoming["overall"].Add(info);
                    }
                }
                PublishMapInfos(incoming, preserveNewerThanUtc: stamp.Write);
                loadedFile = stamp;
                failedFile = null;
                mapCache.Clear();
                failedMapLookups.Clear();
                return true;
            }
            catch (Exception ex)
            {
                failedFile = stamp;
                fileRetryAfterUtc = utcNow().AddSeconds(15);
                Logging.ErrorPrint($"AccSaber map cache refresh failed; keeping previous catalog: {ex.Message}");
                return loadedFile.HasValue;
            }
        }

        private ShortScore CreateMapInfo(string hash, string difficulty, double complexity, string category, string mode, DateTime fetchedAtUtc)
        {
            int value = ParseAccSaberDifficulty(difficulty);
            if (string.IsNullOrEmpty(hash) || value < 0 || double.IsNaN(complexity) || double.IsInfinity(complexity) || complexity < 0)
                throw new InvalidOperationException("AccSaber returned invalid map ratings.");
            mode = mode ?? "Standard";
            if (!mode.StartsWith("Solo", StringComparison.OrdinalIgnoreCase)) mode = "Solo" + mode;
            return new ShortScore(CreateSeachString(hash, mode, value), new PPPStarRating(complexity), fetchedAtUtc.ToLocalTime(), category);
        }

        private void PublishMapInfos(Dictionary<string, List<ShortScore>> incoming, string hash = null, DateTime? preserveNewerThanUtc = null)
        {
            if (preserveNewerThanUtc.HasValue)
            {
                var newer = _dctMapPool.Values.SelectMany(pool =>
                {
                    lock (pool.DataLock) return pool.LsLeaderboadInfo
                        .Where(x => x.FetchTime.ToUniversalTime() > preserveNewerThanUtc.Value).ToArray();
                }).GroupBy(x => x.Searchstring).Select(x => x.OrderByDescending(y => y.FetchTime).First());
                foreach (var info in newer)
                {
                    foreach (var list in incoming.Values) list.RemoveAll(x => x.Searchstring == info.Searchstring);
                    if (incoming.TryGetValue(NormalizeCategoryToPoolId(info.Category), out var category)) category.Add(info);
                    if (incoming.TryGetValue("overall", out var overall)) overall.Add(info);
                }
            }
            foreach (var pair in incoming)
            {
                var pool = _dctMapPool[pair.Key];
                ShortScore[] previous;
                lock (pool.DataLock)
                    previous = pool.LsLeaderboadInfo.Where(x => hash == null || x.Searchstring.StartsWith(hash + "_", StringComparison.OrdinalIgnoreCase)).ToArray();
                var next = pair.Value.GroupBy(x => x.Searchstring).Select(x => x.Last()).ToList();
                foreach (var old in previous)
                {
                    var current = next.FirstOrDefault(x => x.Searchstring == old.Searchstring);
                    MarkRatingChange(old.Searchstring, old.StarRating, current?.StarRating ?? new PPPStarRating(0),
                        NormalizeCategoryToPoolId(old.Category) != NormalizeCategoryToPoolId(current?.Category));
                }
                lock (pool.DataLock)
                {
                    pool.LsLeaderboadInfo.RemoveAll(x => hash == null || x.Searchstring.StartsWith(hash + "_", StringComparison.OrdinalIgnoreCase));
                    pool.LsLeaderboadInfo.AddRange(next);
                    pool.DtUtcLastRefresh = utcNow();
                }
            }
        }

        private void AddLeaderboardInfo(PPPMapPool pool, string hash, string difficulty, double complexity, string category)
        {
            var info = CreateMapInfo(hash, difficulty, complexity, category, "Standard", utcNow());
            ShortScore previous;
            lock (pool.DataLock) previous = pool.LsLeaderboadInfo.FirstOrDefault(x => x.Searchstring == info.Searchstring);
            MarkRatingChange(info.Searchstring, previous?.StarRating, info.StarRating);
            lock (pool.DataLock)
            {
                pool.LsLeaderboadInfo.RemoveAll(x => x.Searchstring == info.Searchstring);
                pool.LsLeaderboadInfo.Add(info);
            }
        }

        private async Task LoadAccSaberMapByHash(string hash)
        {
            if (string.IsNullOrEmpty(hash)) return;
            EnsureAccSaberMapPools();
            var response = await mapCache.Get(hash, async () =>
            {
                var map = await accsaberapi.GetMapByHash(hash);
                if (map == null) return null;
                if (map.difficulties == null) throw new InvalidOperationException("AccSaber returned an invalid map response.");
                foreach (var diff in map.difficulties)
                {
                    if (string.IsNullOrEmpty(NormalizeCategoryToPoolId(diff.categoryId)))
                        throw new InvalidOperationException("AccSaber returned an unknown map category.");
                    CreateMapInfo(hash, diff.difficulty, diff.complexity, diff.categoryId, diff.characteristic, utcNow());
                }
                return map.difficulties.Count == 0 ? null : map;
            });
            if (response.Error != null)
            {
                if (failedMapLookups.Count >= 512) failedMapLookups.Clear();
                failedMapLookups.Add(hash);
                Logging.ErrorPrint($"AccSaber map refresh failed; keeping cached rating: {response.Error.Message}");
                return;
            }
            failedMapLookups.Remove(hash);
            var incoming = _dctMapPool.Values.Where(x => x.Id != unsetPoolId).ToDictionary(x => x.Id, x => new List<ShortScore>());
            foreach (var diff in response.Value?.difficulties ?? new List<AccSaberMapDifficulty>())
            {
                string category = NormalizeCategoryToPoolId(diff.categoryId);
                var info = CreateMapInfo(hash, diff.difficulty, diff.complexity, GetCategoryDisplayName(category),
                    diff.characteristic, response.FetchedAtUtc);
                incoming[category].Add(info);
                incoming["overall"].Add(info);
            }
            PublishMapInfos(incoming, hash);
        }

        private int ParseAccSaberDifficulty(string difficulty)
        {
            return ParsingUtil.ParseDifficultyNameToInt((difficulty ?? string.Empty).Replace("_", string.Empty));
        }

        private string NormalizeCategoryToPoolId(string category)
        {
            string value = (category ?? string.Empty).ToLowerInvariant();
            if (value.Contains("standard")) return "standard";
            if (value.Contains("tech")) return "tech";
            if (value.Contains("true")) return "true";
            return string.Empty;
        }

        private string GetCategoryDisplayName(string categoryId)
        {
            if (_dctMapPool.TryGetValue(categoryId, out PPPMapPool mapPool))
            {
                return mapPool.MapPoolName;
            }

            return categoryId;
        }

        private class AccSaberMapCache
        {
            public List<AccSaberCachedMap> content { get; set; }
        }

        private class AccSaberCachedMap
        {
            public string songHash { get; set; }
            public List<AccSaberCachedDifficulty> difficulties { get; set; }
        }

        private class AccSaberCachedDifficulty
        {
            public string characteristic { get; set; }
            public string difficulty { get; set; }
            public double complexity { get; set; }
            public string categoryCode { get; set; }
        }
    }
}
