using PPPredictor.Core.DataType;
using PPPredictor.Core.DataType.Curve;
using PPPredictor.Core.DataType.MapPool;
using PPPredictor.Core.DataType.Score;
using PPPredictor.Core.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static PPPredictor.Core.DataType.Enums;
using static PPPredictor.Core.DataType.LeaderBoard.BeatLeaderDataTypes;

namespace PPPredictor.Core.Calculator
{
    class PPCalculatorBeatLeader<BLAPI> : PPCalculator where BLAPI : IBeatLeaderAPI, new ()
    {
        private readonly BLAPI beatleaderapi;
        private readonly TimedAsyncCache<BeatLeaderSong> songCache;
        private readonly Func<DateTime> utcNow;
        internal static float accumulationConstant = 0.965f;
        private const float accumulationConstantEvent = 0.925f;
        private const string AccRating = "AccRating";
        private const string PassRating = "PassRating";
        private const string TechRating = "TechRating";
        private string _userName = string.Empty;

        public PPCalculatorBeatLeader(Dictionary<string, PPPMapPool> dctMapPool, Settings settings) : this(dctMapPool, settings, new BLAPI())
        {
        }

        internal PPCalculatorBeatLeader(Dictionary<string, PPPMapPool> dctMapPool, Settings settings, BLAPI api, Func<DateTime> utcNow = null) : base(dctMapPool, settings, Leaderboard.BeatLeader)
        {
            beatleaderapi = api;
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
            songCache = new TimedAsyncCache<BeatLeaderSong>(settings?.MapInfoCacheDuration ?? TimeSpan.FromMinutes(5), this.utcNow);
            foreach (var pool in _dctMapPool.Values.Where(x => x.BeatLeaderPPVersion < 4))
            {
                pool.LsScores.Clear();
                pool.LsLeaderboadInfo.Clear();
                pool.DctScorePositionLookup.Clear();
                pool.HasScoreDataFetched = false;
                pool.DtLastScoreSet = new DateTime(2000, 1, 1);
                pool.DtUtcLastRefresh = new DateTime(2000, 1, 1);
                pool.CurrentPlayer = new PPPPlayer();
                pool.SessionPlayer = null;
                pool.BeatLeaderPPVersion = 4;
            }
        }

        internal override async Task<PPPPlayer> GetPlayerInfo(long userId, PPPMapPool mapPool)
        {
            try
            {
                if(mapPool != null && mapPool.MapPoolType == MapPoolType.Default)
                {
                    BeatLeaderPlayer player = await beatleaderapi.GetPlayer(userId, GetLeaderboardContextId(mapPool.LeaderboardContext));
                    return new PPPPlayer(player);
                }
                else if (mapPool != null && mapPool.MapPoolType == MapPoolType.Custom)
                {
                    if (string.IsNullOrEmpty(_userName)) {
                        BeatLeaderPlayer player = await beatleaderapi.GetPlayer(userId, GetLeaderboardContextId(mapPool.LeaderboardContext));
                        _userName = player.name;
                    }
                    BeatLeaderPlayerList playerList = await beatleaderapi.GetPlayerForEvent(mapPool.Id , _userName);
                    if(playerList != null && playerList.data?.FirstOrDefault(x => x.id == userId) is BeatLeaderPlayer eventPlayer)
                    {
                        return new PPPPlayer(eventPlayer);
                    }
                }
                return new PPPPlayer(true);
            }
            catch (Exception ex)
            {
                Logging.ErrorPrint($"PPCalculatorBeatLeader GetPlayerInfo Error: {ex.Message}");
                return new PPPPlayer(true);
            }
        }

        internal override async Task<List<PPPPlayer>> GetPlayers(double fetchIndexPage, PPPMapPool mapPool)
        {
            try
            {
                BeatLeaderPlayerList scoreSaberPlayerCollection = null;
                List<PPPPlayer> lsPlayer = new List<PPPPlayer>();
                if(mapPool != null && mapPool.MapPoolType == MapPoolType.Default)
                {
                    scoreSaberPlayerCollection = await beatleaderapi.GetPlayersInLeaderboard("pp", (int)fetchIndexPage, _leaderboardInfo.PlayerPerPages, "desc", GetLeaderboardContextId(mapPool.LeaderboardContext));
                }
                else if(mapPool != null)
                {
                    scoreSaberPlayerCollection = await beatleaderapi.GetPlayersInEventLeaderboard(mapPool.Id, "pp", (int)fetchIndexPage, _leaderboardInfo.PlayerPerPages, "desc");
                }
                foreach (var scoreSaberPlayer in scoreSaberPlayerCollection.data)
                {
                    lsPlayer.Add(new PPPPlayer(scoreSaberPlayer));
                }
                return lsPlayer;
            }
            catch (Exception ex)
            {
                Logging.ErrorPrint($"PPCalculatorBeatLeader GetPlayers Error: {ex.Message}");
                return new List<PPPPlayer>();
            }
        }

        internal override async Task<PPPScoreCollection> GetRecentScores(string userId, int pageSize, int page, PPPMapPool mapPool)
        {
            try
            {
                long? eventId = null;
                if (mapPool.MapPoolType == MapPoolType.Custom && !string.IsNullOrEmpty(mapPool.Id))
                {
                    eventId = long.Parse(mapPool.Id);
                }
                BeatLeaderPlayerScoreList beatLeaderPlayerScoreList = await beatleaderapi.GetPlayerScores(userId, "date", "desc", page, pageSize, GetLeaderboardContextId(mapPool.LeaderboardContext), eventId);
                return new PPPScoreCollection(beatLeaderPlayerScoreList);
            }
            catch (Exception ex)
            {
                Logging.ErrorPrint($"PPCalculatorBeatLeader GetRecentScores Error: {ex.Message}");
                return PPPScoreCollection.Failed();
            }
        }

        internal override Task<PPPScoreCollection> GetAllScores(string userId, PPPMapPool mapPool)
        {
            return Task.FromResult(new PPPScoreCollection());
        }

        internal override async Task<PPPBeatMapInfo> GetBeatMapInfoAsync(PPPBeatMapInfo beatMapInfo, PPPMapPool mapPool)
        {
            ShortScore cachedInfo = null;
            try
            {
                if (!string.IsNullOrEmpty(beatMapInfo.CustomLevelHash))
                {
                    string searchString = CreateSeachString(beatMapInfo.CustomLevelHash, "SOLO" + beatMapInfo.BeatmapKey.serializedName, ParsingUtil.ParseDifficultyNameToInt(beatMapInfo.BeatmapKey.difficulty.ToString()));
                    if(mapPool.MapPoolType == MapPoolType.Custom && !mapPool.LsMapPoolEntries.Where(x => x.Searchstring == searchString).Any())
                    {
                        return new PPPBeatMapInfo(beatMapInfo, new PPPStarRating(0)); //Currently selected map is not contained in selected MapPool
                    }
                    lock (mapPool.DataLock) cachedInfo = mapPool.LsLeaderboadInfo?.FirstOrDefault(x => x.Searchstring == searchString);
                    TimeSpan ttl = cachedInfo?.StarRating.IsRanked() == true ? _settings.MapInfoCacheDuration : TimeSpan.FromMinutes(1);
                    TimeSpan age = cachedInfo == null ? TimeSpan.MaxValue : utcNow() - cachedInfo.FetchTime.ToUniversalTime();
                    if (cachedInfo != null && age >= TimeSpan.Zero && age < ttl)
                        return new PPPBeatMapInfo(beatMapInfo, cachedInfo.StarRating);

                    var response = await songCache.Get(beatMapInfo.CustomLevelHash, async () =>
                    {
                        var song = await beatleaderapi.GetSongByHash(beatMapInfo.CustomLevelHash);
                        if (song != null && (song.difficulties == null || song.difficulties.Count == 0))
                            throw new InvalidOperationException("BeatLeader returned an invalid map response.");
                        return song;
                    });
                    if (response.Error != null)
                    {
                        Logging.ErrorPrint($"BeatLeader map refresh failed; keeping cached rating: {response.Error.Message}");
                        return new PPPBeatMapInfo(beatMapInfo, cachedInfo?.StarRating ?? new PPPStarRating(-1)) { MapInfoRefreshFailed = true };
                    }
                    BeatLeaderDifficulty diff = response.Value?.difficulties.FirstOrDefault(x =>
                        x.value == (int)beatMapInfo.BeatmapKey.difficulty && string.Equals(x.modeName, beatMapInfo.BeatmapKey.serializedName, StringComparison.OrdinalIgnoreCase));
                    var rating = diff == null ? new PPPStarRating(0)
                        : new PPPStarRating(diff, mapPool.MapPoolType == MapPoolType.Custom ? (bool?)diff.stars.HasValue : null);
                    MarkRatingChange(searchString, cachedInfo?.StarRating, rating);
                    lock (mapPool.DataLock)
                    {
                        mapPool.LsLeaderboadInfo.RemoveAll(x => x.Searchstring == searchString);
                        mapPool.LsLeaderboadInfo.Add(new ShortScore(searchString, rating, response.FetchedAtUtc.ToLocalTime()));
                    }
                    return new PPPBeatMapInfo(beatMapInfo, rating);
                }
                return new PPPBeatMapInfo(beatMapInfo, new PPPStarRating(0));
            }
            catch (Exception ex)
            {
                Logging.ErrorPrint($"PPCalculatorBeatLeader GetStarsForBeatmapAsync Error: {ex.Message}");
                return new PPPBeatMapInfo(beatMapInfo, cachedInfo?.StarRating ?? new PPPStarRating(-1)) { MapInfoRefreshFailed = true };
            }
        }

        internal override PPPBeatMapInfo ApplyModifiersToBeatmapInfo(PPPBeatMapInfo beatMapInfo, PPPMapPool mapPool, DataType.BeatSaberEncapsulation.GameplayModifiers gameplayModifiers, bool levelFailed, bool levelPaused)
        {
            List<string> lsModifiers = ParseModifiers(gameplayModifiers);
            double accRating = beatMapInfo.BaseStarRating.AccRating;
            double passRating = beatMapInfo.BaseStarRating.PassRating;
            double techRating = beatMapInfo.BaseStarRating.TechRating;
            double predictedAcc = beatMapInfo.BaseStarRating.PredictedAcc;
            double sliderPercentage = beatMapInfo.BaseStarRating.SliderPercentage;
            double? noteSpan = beatMapInfo.BaseStarRating.NoteSpan;
            if (beatMapInfo.BaseStarRating.ModifiersRating != null)
            {
                foreach (string modifier in lsModifiers.Select(x => x.ToLower()))
                {
                    if (beatMapInfo.BaseStarRating.ModifiersRating.ContainsKey(modifier + AccRating))
                    {
                        accRating = beatMapInfo.BaseStarRating.ModifiersRating[modifier + AccRating];
                        passRating = beatMapInfo.BaseStarRating.ModifiersRating[modifier + PassRating];
                        techRating = beatMapInfo.BaseStarRating.ModifiersRating[modifier + TechRating];
                        if (modifier == "ss" || modifier == "fs" || modifier == "sf")
                        {
                            beatMapInfo.BaseStarRating.ModifiersRating.TryGetValue(modifier + "PredictedAcc", out predictedAcc);
                            beatMapInfo.BaseStarRating.ModifiersRating.TryGetValue(modifier + "SliderPercentage", out sliderPercentage);
                            noteSpan /= modifier == "ss" ? 0.85 : modifier == "fs" ? 1.2 : 1.5;
                        }

                        break;
                    }
                }
            }
            double multiplier = GenerateModifierMultiplier(lsModifiers, beatMapInfo, mapPool, levelFailed, levelPaused,
                beatMapInfo.BaseStarRating.ModifiersRating != null, out var blockReason);
            beatMapInfo.CalculationBlockReason = blockReason;
            beatMapInfo.ModifiedStarRating = new PPPStarRating(multiplier, accRating, passRating, techRating, beatMapInfo.BaseStarRating.RankedBeatLeader)
            {
                PredictedAcc = predictedAcc,
                SliderPercentage = sliderPercentage,
                Notes = beatMapInfo.BaseStarRating.Notes,
                NoteSpan = noteSpan
            };
            return beatMapInfo;
        }

        private List<string> ParseModifiers(DataType.BeatSaberEncapsulation.GameplayModifiers gameplayModifiers)
        {
            try
            {
                List<string> lsModifiers = new List<string>();
                if (gameplayModifiers.disappearingArrows) lsModifiers.Add("DA");
                if (gameplayModifiers.songSpeed == DataType.BeatSaberEncapsulation.GameplayModifiers.SongSpeed.Faster) lsModifiers.Add("FS");
                if (gameplayModifiers.songSpeed == DataType.BeatSaberEncapsulation.GameplayModifiers.SongSpeed.Slower) lsModifiers.Add("SS");
                if (gameplayModifiers.songSpeed == DataType.BeatSaberEncapsulation.GameplayModifiers.SongSpeed.SuperFast) lsModifiers.Add("SF");
                if (gameplayModifiers.ghostNotes) lsModifiers.Add("GN");
                if (gameplayModifiers.noArrows) lsModifiers.Add("NA");
                if (gameplayModifiers.noBombs) lsModifiers.Add("NB");
                if (gameplayModifiers.noFailOn0Energy) lsModifiers.Add("NF");
                if (gameplayModifiers.enabledObstacleType == DataType.BeatSaberEncapsulation.GameplayModifiers.EnabledObstacleType.NoObstacles) lsModifiers.Add("NO");
                if (gameplayModifiers.proMode) lsModifiers.Add("PM");
                if (gameplayModifiers.smallCubes) lsModifiers.Add("SC");
                if (gameplayModifiers.instaFail) lsModifiers.Add("IF");
                if (gameplayModifiers.energyType == DataType.BeatSaberEncapsulation.GameplayModifiers.EnergyType.Battery) lsModifiers.Add("BE");
                if (gameplayModifiers.strictAngles) lsModifiers.Add("SA");
                if (gameplayModifiers.zenMode) lsModifiers.Add("ZM");
                return lsModifiers;
            }
            catch (Exception ex)
            {
                Logging.ErrorPrint($"PPCalculatorBeatLeader ParseModifiers Error: {ex.Message}");
                return new List<string>();
            }
        }

        private double GenerateModifierMultiplier(List<string> lsModifier, PPPBeatMapInfo beatMapInfo, PPPMapPool mapPool, bool levelFailed, bool levelPaused, bool ignoreSpeedMultiplier, out PPCalculationBlockReason blockReason)
        {
            blockReason = PPCalculationBlockReason.None;
            try
            {
                double multiplier = 1;
                if (mapPool.LeaderboardContext == LeaderboardContext.BeatLeaderSCPM && !(lsModifier.Contains("SC") && lsModifier.Contains("PM")))
                {
                    blockReason = PPCalculationBlockReason.RequiresSmallCubesAndProMode;
                    return 0;
                }
                foreach (string modifier in lsModifier)
                {
                    if (mapPool.LeaderboardContext == LeaderboardContext.BeatLeaderNoModifiers && !(modifier == "BE" || modifier == "IF"))
                    {
                        blockReason = PPCalculationBlockReason.ModifiersNotAllowed;
                        return 0;
                    }
                    if (!levelFailed && modifier == "NF") continue; //Ignore nofail until the map is failed in gameplay
                    if (ignoreSpeedMultiplier && (modifier == "SF" || modifier == "SS" || modifier == "FS")) continue; //Ignore speed multies and use the precomputed values from backend
                    if(beatMapInfo.BaseStarRating.ModifierValues.TryGetValue(modifier.ToLower(), out double value))
                    {
                        if (mapPool.LeaderboardContext == LeaderboardContext.BeatLeaderGolf && value < 0)
                        {
                            blockReason = PPCalculationBlockReason.NegativeModifiersNotAllowed;
                            return 0;
                        }
                        multiplier += value;
                    }
                }
                return multiplier;
            }
            catch (Exception ex)
            {
                Logging.ErrorPrint($"PPCalculatorBeatLeader GenerateModifierMultiplier Error: {ex.Message}");
                return -1;
            }
        }

        override internal async Task InternalUpdateMapPoolDetails(PPPMapPool mapPool)
        {
            if (mapPool.MapPoolType != MapPoolType.Default)
            {
                mapPool.LsMapPoolEntries.Clear();
                BeatLeaderPlayListSongList lsPlayList = await this.beatleaderapi.GetSongsInPlaylistById(long.Parse(mapPool.PlayListId));
                foreach (BeatLeaderSong song in lsPlayList.data)
                {
                    foreach (BeatLeaderDifficulty diff in song.difficulties)
                    {
                        string searchString = CreateSeachString(song.hash, "SOLO" + diff.modeName, diff.value);
                        mapPool.LsMapPoolEntries.Add(new PPPMapPoolEntry(searchString));
                    }
                }
            }
        }

        public override string CreateSeachString(string hash, DataType.BeatSaberEncapsulation.BeatmapKey beatmapKey)
        {
            return $"{hash}_{ParsingUtil.ParseDifficultyNameToInt(beatmapKey.difficulty.ToString())}";
        }

        private long GetLeaderboardContextId(LeaderboardContext leaderboardContext)
        {
            switch (leaderboardContext)
            {
                case LeaderboardContext.BeatLeaderDefault: return 2;
                case LeaderboardContext.BeatLeaderNoModifiers: return 4;
                case LeaderboardContext.BeatLeaderNoPauses: return 8;
                case LeaderboardContext.BeatLeaderGolf: return 16;
                case LeaderboardContext.BeatLeaderSCPM: return 32;
                default: return 0;
            }
        }

        override public async Task UpdateAvailableMapPools()
        {
            EnsureBeatLeaderMapPool("-1", "General", 0, LeaderboardContext.BeatLeaderDefault);
            EnsureBeatLeaderMapPool("-2", "No modifiers", 1, LeaderboardContext.BeatLeaderNoModifiers);
            EnsureBeatLeaderMapPool("-3", "No pauses", 2, LeaderboardContext.BeatLeaderNoPauses);
            EnsureBeatLeaderMapPool("-4", "Golf", 3, LeaderboardContext.BeatLeaderGolf);
            EnsureBeatLeaderMapPool("-5", "SCPM", 4, LeaderboardContext.BeatLeaderSCPM);

            //BeatLeaderEventList beatLeaderEventList = await beatleaderapi.GetEvents();
            BeatLeaderEventList beatLeaderEventList = new BeatLeaderEventList();
            DateTime dateCutoff = DateTime.UtcNow;
#if DEBUG
            //dateCutoff = dateCutoff.AddDays(-120);
#endif
            foreach (BeatLeaderEvent blEvent in beatLeaderEventList.data)
            {
                if (_dctMapPool.TryGetValue(blEvent.id.ToString(), out PPPMapPool mapPool))
                {
                    if (blEvent.dtEndDate < dateCutoff)
                    {
                        _dctMapPool.Remove(blEvent.id.ToString());
                    }
                }
                else
                {
                    if (blEvent.dtEndDate >= dateCutoff)
                    {
                        PPPMapPool insertMapPool = new PPPMapPool(blEvent.id.ToString(), blEvent.playListId.ToString(), MapPoolType.Custom, $"Event {blEvent.name}", accumulationConstantEvent, (int.MaxValue - blEvent.id), new BeatLeaderPPPCurve(), blEvent.image) { BeatLeaderPPVersion = 4 };
                        if (!_dctMapPool.ContainsKey(insertMapPool.Id)) _dctMapPool.Add(insertMapPool.Id, insertMapPool);
                    }
                }
            }
            SendMapPoolRefreshed();
            return;
        }

        private void EnsureBeatLeaderMapPool(string id, string name, int sortIndex, LeaderboardContext leaderboardContext)
        {
            if (!_dctMapPool.TryGetValue(id, out PPPMapPool mapPool))
            {
                _dctMapPool.Add(id, new PPPMapPool(id, MapPoolType.Default, name, accumulationConstant, sortIndex, new BeatLeaderPPPCurve(), leaderboardContext) { BeatLeaderPPVersion = 4 });
                return;
            }

            mapPool.MapPoolType = MapPoolType.Default;
            mapPool.MapPoolName = name;
            mapPool.AccumulationConstant = accumulationConstant;
            mapPool.SortIndex = sortIndex;
            mapPool.Curve = new BeatLeaderPPPCurve();
            mapPool.LeaderboardContext = leaderboardContext;
        }

        internal override bool IsScoreSetOnCurrentMapPool(PPPMapPool mapPool, PPPScoreSetData score)
        {
            return (GetLeaderboardContextId(mapPool.LeaderboardContext) & score.context) > 0;
        }

        internal override List<PPPMapPoolShort> GetMapPools()
        {
            return _dctMapPool.Values.OrderBy(x => x.SortIndex).Select(x => (PPPMapPoolShort)x).ToList();
        }

        internal override string GetStarDisplay(PPPBeatMapInfo beatMapInfo)
        {
            return $"💪{FormatStarRating(beatMapInfo.ModifiedStarRating?.PassRating)} 🎯{FormatStarRating(beatMapInfo.ModifiedStarRating?.AccRating)} 🔧{FormatStarRating(beatMapInfo.ModifiedStarRating?.TechRating)}";
        }

        internal override PPPMapPoolShort FindPoolWithPlayListId(string playlistId)
        {
            return _dctMapPool.Values.FirstOrDefault(x => x.PlayListId == playlistId);
        }
    }
}
