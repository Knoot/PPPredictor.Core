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
        private bool accSaberMapCacheLoaded;
        private readonly HashSet<string> accSaberMapLookupMisses = new HashSet<string>();

        public PPCalculatorAccSaber(Dictionary<string, PPPMapPool> dctMapPool, Settings settings) : this(dctMapPool, settings, new ASAPI())
        {
        }

        internal PPCalculatorAccSaber(Dictionary<string, PPPMapPool> dctMapPool, Settings settings, ASAPI api) : base(dctMapPool, settings, Leaderboard.AccSaber)
        {
            accsaberapi = api;
        }
        internal override Task<PPPBeatMapInfo> GetBeatMapInfoAsync(PPPBeatMapInfo beatMapInfo, PPPMapPool mapPool)
        {
            try
            {
                if (!string.IsNullOrEmpty(beatMapInfo.CustomLevelHash))
                {
                    string searchString = CreateSeachString(beatMapInfo.CustomLevelHash, beatMapInfo.BeatmapKey);
                    ShortScore cachedInfo;
                    lock (mapPool.DataLock) cachedInfo = mapPool.LsLeaderboadInfo.FirstOrDefault(x => x.Searchstring == searchString);
                    if(cachedInfo != null)
                    {
                        return Task.FromResult(new PPPBeatMapInfo(beatMapInfo, new PPPStarRating(cachedInfo.StarRating.Stars)));
                    }
                    return Task.FromResult(new PPPBeatMapInfo(beatMapInfo, new PPPStarRating(0)));
                }
                return Task.FromResult(beatMapInfo);
            }
            catch (Exception ex)
            {
                Logging.ErrorPrint($"PPCalculatorAccSaber GetBeatMapInfoAsync Error: {ex.Message}");
                return Task.FromResult(new PPPBeatMapInfo(beatMapInfo, new PPPStarRating(-1)));
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
                return mapPool.Id != unsetPoolId && overallScoresLoaded;
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
            if (mapPool != null || beatMapInfo == null || string.IsNullOrEmpty(beatMapInfo.CustomLevelHash))
            {
                return mapPool;
            }

            await LoadAccSaberMapByHash(beatMapInfo.CustomLevelHash);
            return FindMapPoolForBeatMapInfo(beatMapInfo);
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
            if (accSaberMapCacheLoaded)
            {
                return true;
            }
            if (string.IsNullOrEmpty(_settings.AccSaberMapCachePath) || !File.Exists(_settings.AccSaberMapCachePath))
            {
                return false;
            }

            try
            {
                AccSaberMapCache mapCache = JsonConvert.DeserializeObject<AccSaberMapCache>(File.ReadAllText(_settings.AccSaberMapCachePath));
                if (mapCache?.content == null)
                {
                    return false;
                }

                EnsureAccSaberMapPools();
                foreach (PPPMapPool mapPool in _dctMapPool.Values)
                {
                    if (mapPool.Id == unsetPoolId)
                    {
                        continue;
                    }
                    lock (mapPool.DataLock) mapPool.LsLeaderboadInfo.Clear();
                }

                foreach (AccSaberCachedMap map in mapCache.content)
                {
                    foreach (AccSaberCachedDifficulty difficulty in map.difficulties ?? new List<AccSaberCachedDifficulty>())
                    {
                        string categoryId = NormalizeCategoryToPoolId(difficulty.categoryCode);
                        string categoryName = GetCategoryDisplayName(categoryId);
                        if (_dctMapPool.TryGetValue(categoryId, out PPPMapPool categoryPool))
                        {
                            AddLeaderboardInfo(categoryPool, map.songHash, difficulty.difficulty, difficulty.complexity, categoryName);
                        }
                        if (_dctMapPool.TryGetValue("overall", out PPPMapPool overallPool))
                        {
                            AddLeaderboardInfo(overallPool, map.songHash, difficulty.difficulty, difficulty.complexity, categoryName);
                        }
                    }
                }

                DateTime refreshTime = DateTime.UtcNow;
                foreach (PPPMapPool mapPool in _dctMapPool.Values)
                {
                    if (mapPool.Id != unsetPoolId && mapPool.LsLeaderboadInfo.Count > 0)
                    {
                        mapPool.DtUtcLastRefresh = refreshTime;
                    }
                }
                accSaberMapCacheLoaded = true;
                return true;
            }
            catch (Exception ex)
            {
                Logging.ErrorPrint($"PPCalculatorAccSaber LoadAccSaberMapCache Error: {ex.Message}");
                return false;
            }
        }

        private void AddLeaderboardInfo(PPPMapPool mapPool, string songHash, string difficulty, double complexity, string category)
        {
            string searchString = CreateSeachString(songHash, "SoloStandard", ParseAccSaberDifficulty(difficulty));
            lock (mapPool.DataLock)
            {
                if (mapPool.LsLeaderboadInfo.Any(x => x.Searchstring == searchString))
                {
                    return;
                }

                mapPool.LsLeaderboadInfo.Add(new ShortScore(searchString, new PPPStarRating(complexity), DateTime.Now, category));
            }
        }

        private async Task LoadAccSaberMapByHash(string hash)
        {
            string normalizedHash = hash?.ToUpperInvariant();
            if (string.IsNullOrEmpty(normalizedHash) || accSaberMapLookupMisses.Contains(normalizedHash))
            {
                return;
            }

            try
            {
                EnsureAccSaberMapPools();
                AccSaberMap map = await accsaberapi.GetMapByHash(normalizedHash);
                if (map?.difficulties == null || map.difficulties.Count == 0)
                {
                    accSaberMapLookupMisses.Add(normalizedHash);
                    return;
                }

                string songHash = string.IsNullOrEmpty(map.songHash) ? normalizedHash : map.songHash;
                foreach (AccSaberMapDifficulty difficulty in map.difficulties)
                {
                    string categoryId = NormalizeCategoryToPoolId(difficulty.categoryId);
                    string categoryName = GetCategoryDisplayName(categoryId);
                    if (_dctMapPool.TryGetValue(categoryId, out PPPMapPool categoryPool))
                    {
                        AddLeaderboardInfo(categoryPool, songHash, difficulty.difficulty, difficulty.complexity, categoryName);
                    }
                    if (_dctMapPool.TryGetValue("overall", out PPPMapPool overallPool))
                    {
                        AddLeaderboardInfo(overallPool, songHash, difficulty.difficulty, difficulty.complexity, categoryName);
                    }
                }
            }
            catch (Exception ex)
            {
                accSaberMapLookupMisses.Add(normalizedHash);
                Logging.ErrorPrint($"PPCalculatorAccSaber LoadAccSaberMapByHash Error: {ex.Message}");
            }
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
            public string difficulty { get; set; }
            public double complexity { get; set; }
            public string categoryCode { get; set; }
        }
    }
}
