using PPPredictor.Core;
using PPPredictor.Core.Calculator;
using PPPredictor.Core.DataType;
using PPPredictor.Core.DataType.BeatSaberEncapsulation;
using PPPredictor.Core.DataType.MapPool;
using PPPredictor.Core.DataType.Score;
using PPPredictor.Core.Interface;
using PPPredictor.Core.UnitTest.MockService;
using UnitTests.LeaderBoard;
using static PPPredictor.Core.DataType.Enums;
using static PPPredictor.Core.DataType.LeaderBoard.BeatLeaderDataTypes;
using static PPPredictor.Core.DataType.LeaderBoard.ScoreSaberDataTypes;
using static PPPredictor.Core.DataType.LeaderBoard.AccSaberDataTypes;

namespace UnitTests.Data
{
    [TestClass]
    public class TestMapRatingRefresh
    {
        internal sealed class BeatLeaderApi : MockBeatLeader, IBeatLeaderAPI
        {
            internal Func<BeatLeaderSong> Fetch = () => Song(5);
            internal int Calls;
            Task<BeatLeaderSong> IBeatLeaderAPI.GetSongByHash(string hash) { Calls++; return Task.FromResult(Fetch()); }
        }

        internal sealed class ScoreSaberApi : MockScoreSaberApi, IScoresaberAPI
        {
            internal Func<string, int, string, ScoreSaberLeaderboardInfo> Fetch = (hash, diff, mode) => new()
            {
                songHash = hash, stars = 5, ranked = true,
                difficulty = new ScoreSaberDifficulty { difficulty = diff, gameMode = mode }
            };
            internal int Calls;
            Task<ScoreSaberLeaderboardInfo> IScoresaberAPI.GetLeaderboardInfo(string hash, int difficulty, string mode)
            { Calls++; return Task.FromResult(Fetch(hash, difficulty, mode)); }
        }

        private static Settings Settings() => new(false, true, false, true, "123", PPGainCalculationType.Weighted,
            MapPoolSorting.Alphabetical, "", -7, DateTime.Now.AddHours(-1), 12);
        private static PPPBeatMapInfo Map(string mode = "Standard") => new("HASH", new BeatmapKey
            { serializedName = mode, difficulty = BeatMapDifficulty.ExpertPlus });
        private static BeatLeaderSong Song(double rating) => new()
        {
            difficulties = new List<BeatLeaderDifficulty> { new() { value = 9, modeName = "Standard",
                status = (int)BeatLeaderDifficultyStatus.ranked, accRating = rating, passRating = rating, techRating = rating } }
        };

        [TestMethod]
        public async Task BeatLeaderRefreshesExpiredRatingsAndInvalidatesPlayedScores()
        {
            var now = DateTime.UtcNow;
            var pool = new PPPMapPool { BeatLeaderPPVersion = 4, MapPoolType = MapPoolType.Default };
            pool.LsScores.Add(new ShortScore("HASH_SOLOSTANDARD_9", 100));
            var api = new BeatLeaderApi();
            var calculator = new PPCalculatorBeatLeader<BeatLeaderApi>(new() { [pool.Id] = pool }, Settings(), api, () => now);
            Assert.AreEqual(5, (await calculator.GetBeatMapInfoAsync(Map(), pool)).BaseStarRating.AccRating);
            now = now.AddMinutes(4);
            await calculator.GetBeatMapInfoAsync(Map(), pool);
            Assert.AreEqual(1, api.Calls);
            now = now.AddMinutes(1);
            api.Fetch = () => Song(6);
            Assert.AreEqual(6, (await calculator.GetBeatMapInfoAsync(Map(), pool)).BaseStarRating.AccRating);
            Assert.AreEqual(2, api.Calls);
            Assert.IsTrue(pool.ScoresRequireRefresh);
            Assert.IsFalse(calculator.HasScoreDataForPPGain(pool));
        }

        [TestMethod]
        public async Task BeatLeaderFailureKeepsRatingButConfirmedAbsenceRemovesIt()
        {
            var now = DateTime.UtcNow;
            var pool = new PPPMapPool { BeatLeaderPPVersion = 4, MapPoolType = MapPoolType.Default };
            var api = new BeatLeaderApi();
            var calculator = new PPCalculatorBeatLeader<BeatLeaderApi>(new() { [pool.Id] = pool }, Settings(), api, () => now);
            await calculator.GetBeatMapInfoAsync(Map(), pool);
            now = now.AddMinutes(5);
            api.Fetch = () => throw new IOException("offline");
            var stale = await calculator.GetBeatMapInfoAsync(Map(), pool);
            Assert.AreEqual(5, stale.BaseStarRating.AccRating);
            Assert.IsTrue(stale.MapInfoRefreshFailed);
            now = now.AddSeconds(15);
            api.Fetch = () => null!;
            var missing = await calculator.GetBeatMapInfoAsync(Map(), pool);
            Assert.IsFalse(missing.BaseStarRating.IsRanked());
            Assert.IsFalse(missing.MapInfoRefreshFailed);
        }

        [TestMethod]
        public async Task ScoreSaberQueriesExactModeAndRefreshesRatingsWithoutSlidingExpiry()
        {
            var now = DateTime.UtcNow;
            var pool = new PPPMapPool();
            pool.LsScores.Add(new ShortScore("HASH_SOLOSTANDARD_9", 100));
            var api = new ScoreSaberApi();
            var calculator = new PPCalculatorScoreSaber<ScoreSaberApi>(new() { [pool.Id] = pool }, Settings(),
                _ => throw new Exception("live rating must take precedence"), api, () => now);
            await calculator.GetBeatMapInfoAsync(Map(), pool);
            now = now.AddMinutes(4);
            await calculator.GetBeatMapInfoAsync(Map(), pool);
            Assert.AreEqual(1, api.Calls);
            await calculator.GetBeatMapInfoAsync(Map("OneSaber"), pool);
            Assert.AreEqual(2, api.Calls);
            now = now.AddMinutes(1);
            api.Fetch = (hash, diff, mode) => new ScoreSaberLeaderboardInfo { songHash = hash, ranked = false,
                difficulty = new ScoreSaberDifficulty { difficulty = diff, gameMode = mode } };
            Assert.IsFalse((await calculator.GetBeatMapInfoAsync(Map(), pool)).BaseStarRating.IsRanked());
            Assert.IsTrue(pool.ScoresRequireRefresh);
        }

        [TestMethod]
        public async Task ScoreSaberCatalogFallbackIsNeverUsedForOtherModes()
        {
            var pool = new PPPMapPool();
            var api = new ScoreSaberApi { Fetch = (_, _, _) => throw new IOException("offline") };
            var calculator = new PPCalculatorScoreSaber<ScoreSaberApi>(new() { [pool.Id] = pool }, Settings(),
                map => new PPPBeatMapInfo(map, new PPPStarRating(10)), api);
            var standard = await calculator.GetBeatMapInfoAsync(Map(), pool);
            Assert.AreEqual(10, standard.BaseStarRating.Stars);
            Assert.IsTrue(standard.MapInfoRefreshFailed);
            Assert.IsFalse((await calculator.GetBeatMapInfoAsync(Map("OneSaber"), pool)).BaseStarRating.IsRanked());
        }

        private static AccSaberMap AccMap(string category, double complexity) => new()
        {
            difficulties = new List<AccSaberMapDifficulty> { new() { categoryId = category,
                difficulty = "EXPERT_PLUS", complexity = complexity, characteristic = "Standard" } }
        };

        [TestMethod]
        public async Task AccSaberFailureIsRetryableAndCategoryChangesRemoveOldRating()
        {
            var now = DateTime.UtcNow;
            int calls = 0;
            var api = new TestAccSaber.Api { Map = _ => { calls++; throw new IOException("offline"); } };
            var pools = new Dictionary<string, PPPMapPool>();
            var calculator = new PPCalculatorAccSaber<TestAccSaber.Api>(pools, Settings(), api, () => now);
            await calculator.UpdateAvailableMapPools();
            var failed = await calculator.GetBeatMapInfoAsync(Map(), pools["tech"]);
            Assert.IsTrue(failed.MapInfoRefreshFailed);
            Assert.AreEqual(1, calls);
            api.Map = _ => { calls++; return AccMap("Tech", 5); };
            now = now.AddSeconds(15);
            Assert.AreEqual(5, (await calculator.GetBeatMapInfoAsync(Map(), pools["tech"])).BaseStarRating.Stars);
            pools["tech"].LsScores.Add(new ShortScore("HASH_SOLOSTANDARD_9", 100));
            now = now.AddMinutes(5);
            api.Map = _ => AccMap("True", 6);
            Assert.AreEqual("true", (await calculator.FindMapPoolForBeatMapInfoAsync(Map())).Id);
            Assert.IsFalse((await calculator.GetBeatMapInfoAsync(Map(), pools["tech"])).BaseStarRating.IsRanked());
            Assert.AreEqual(6, (await calculator.GetBeatMapInfoAsync(Map(), pools["true"])).BaseStarRating.Stars);
            Assert.IsTrue(pools["tech"].ScoresRequireRefresh);
        }

        [TestMethod]
        public async Task AccSaberFileReloadIsAtomicAndPreservesNewerApiCategory()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
            var now = DateTime.UtcNow;
            var settings = Settings();
            settings.AccSaberMapCachePath = path;
            var pools = new Dictionary<string, PPPMapPool>();
            var api = new TestAccSaber.Api { Map = _ => AccMap("True", 8) };
            var calculator = new PPCalculatorAccSaber<TestAccSaber.Api>(pools, settings, api, () => now);
            void Write(string body, DateTime timestamp)
            {
                File.WriteAllText(path, body);
                File.SetLastWriteTimeUtc(path, timestamp);
            }
            string Catalog(int complexity) => "{\"content\":[{\"songHash\":\"HASH\",\"difficulties\":[{" +
                "\"categoryCode\":\"TECH\",\"difficulty\":\"EXPERT_PLUS\",\"complexity\":" + complexity + "}]}]}";
            try
            {
                Write(Catalog(5), now);
                await calculator.UpdateAvailableMapPools();
                Assert.AreEqual(5, (await calculator.GetBeatMapInfoAsync(Map(), pools["tech"])).BaseStarRating.Stars);
                now = now.AddSeconds(1);
                Write("{broken", now);
                Assert.AreEqual(5, (await calculator.GetBeatMapInfoAsync(Map(), pools["tech"])).BaseStarRating.Stars);
                now = now.AddSeconds(1);
                Write(Catalog(6), now);
                Assert.AreEqual(6, (await calculator.GetBeatMapInfoAsync(Map(), pools["tech"])).BaseStarRating.Stars);
                now = now.AddMinutes(5);
                Assert.AreEqual("true", (await calculator.FindMapPoolForBeatMapInfoAsync(Map())).Id);
                Write(Catalog(7), now.AddMinutes(-1));
                Assert.AreEqual("true", (await calculator.FindMapPoolForBeatMapInfoAsync(Map())).Id);
                Assert.IsFalse((await calculator.GetBeatMapInfoAsync(Map(), pools["tech"])).BaseStarRating.IsRanked());
                Assert.AreEqual(8, (await calculator.GetBeatMapInfoAsync(Map(), pools["true"])).BaseStarRating.Stars);
            }
            finally { File.Delete(path); }
        }
    }
}
