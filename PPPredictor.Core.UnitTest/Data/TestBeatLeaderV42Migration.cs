using Newtonsoft.Json;
using PPPredictor.Core;
using PPPredictor.Core.Calculator;
using PPPredictor.Core.DataType;
using PPPredictor.Core.DataType.Curve;
using PPPredictor.Core.DataType.MapPool;
using PPPredictor.Core.DataType.Score;
using PPPredictor.Core.Interface;
using PPPredictor.Core.UnitTest.MockService;
using static PPPredictor.Core.DataType.Enums;
using static PPPredictor.Core.DataType.LeaderBoard.BeatLeaderDataTypes;

namespace UnitTests.Data
{
    [TestClass]
    public class TestBeatLeaderV42Migration
    {
        internal sealed class Api : MockBeatLeader, IBeatLeaderAPI
        {
            Task<BeatLeaderPlayerScoreList> IBeatLeaderAPI.GetPlayerScores(string id, string sort, string order,
                int page, int count, long context, long? eventId) => Task.FromResult(new BeatLeaderPlayerScoreList
                {
                    metadata = new BeatLeaderPlayerScoreListMetaData { page = page, itemsPerPage = count, total = 1 },
                    data = new() { new BeatLeaderPlayerScore { pp = 90, timeset = "1700000000",
                        leaderboard = new BeatLeaderLeaderboard { song = new BeatLeaderSong { hash = "OLD" },
                            difficulty = new BeatLeaderDifficulty { value = 9, modeName = "Standard", status = 3 } } } }
                });
        }

        private static Settings Settings() => new(false, true, false, false, "123", PPGainCalculationType.Weighted,
            MapPoolSorting.Alphabetical, "", -7, DateTime.Now.AddHours(-1), 12);

        private static PPPMapPool Pool() => new()
        {
            BeatLeaderPPVersion = 4, AccumulationConstant = 0.965f,
            CurveInfo = new CurveInfo(CurveType.BeatLeader),
            MapPoolType = MapPoolType.Default, LeaderboardContext = LeaderboardContext.BeatLeaderDefault,
            CurrentPlayer = new PPPPlayer { Pp = 150 }, SessionPlayer = new PPPPlayer { Pp = 100 },
            LsScores = new() { new ShortScore("OLD_SOLOSTANDARD_9", 150) },
            LsLeaderboadInfo = new() { new ShortScore("OLD_SOLOSTANDARD_9", new PPPStarRating(10), DateTime.UtcNow) }
        };

        [TestMethod]
        public async Task MigrationConfirmsNerfedPendingScoreAndRebasesOnlyBeatLeaderSession()
        {
            var pool = Pool();
            pool.ScoreSync.ExpectedScores["OLD_SOLOSTANDARD_9"] = new PPPScoreSetData { hash = "OLD_SOLOSTANDARD_9", pp = 150 };
            var settings = Settings();
            var sessionReset = settings.LastSessionReset;
            var calculator = new PPCalculatorBeatLeader<Api>(new() { [pool.Id] = pool }, settings);
            try
            {
                Assert.IsFalse(calculator.HasScoreDataForPPGain(pool));
                Assert.IsNull(pool.ScoreSync.ExpectedScores.Values.Single().pp);
                Assert.AreEqual(FullScoreSyncReason.RatingChanged, pool.ScoreSync.PendingReason);
                Assert.AreEqual(150d, pool.LsScores.Single().Pp);
                Assert.AreEqual(0, pool.LsLeaderboadInfo.Count);
                calculator.CommitPlayer(pool, new PPPPlayer { Pp = 140 }, false);
                Assert.IsNull(pool.SessionPlayer, "Profile-only refresh cannot establish a compatible baseline.");
                Assert.IsTrue(await calculator.GetPlayerScores(pool, 10, 100, forceFullRefresh: true,
                    requireAllExpected: true, onCommitted: () => calculator.CommitPlayer(pool, new PPPPlayer { Pp = 90 }, false)));
                Assert.AreEqual(90d, pool.LsScores.Single().Pp);
                Assert.AreEqual(0, pool.ScoreSync.ExpectedScores.Count);
                Assert.AreEqual(90d, pool.SessionPlayer.Pp);
                Assert.IsFalse(pool.BeatLeaderSessionRebasePending);
                Assert.IsTrue(calculator.HasScoreDataForPPGain(pool));
                Assert.AreEqual(sessionReset, settings.LastSessionReset, "Migration must not reset other leaderboards' sessions.");
            }
            finally { calculator.Dispose(); }
        }

        [TestMethod]
        public async Task FailedSyncKeepsSnapshotAndMigrationStateAcrossRecreation()
        {
            var pool = Pool();
            var settings = Settings();
            var calculator = new PPCalculatorBeatLeader<MockBeatLeader>(new() { [pool.Id] = pool }, settings);
            try
            {
                Assert.IsFalse(await calculator.GetPlayerScores(pool, 10, 100, forceFullRefresh: true));
                Assert.AreEqual(150d, pool.LsScores.Single().Pp);
                Assert.IsTrue(pool.ScoresRequireRefresh);
                pool.ScoreSync.ExpectedScores["NEW_SOLOSTANDARD_9"] = new PPPScoreSetData { hash = "NEW_SOLOSTANDARD_9", pp = 200 };
                var restored = JsonConvert.DeserializeObject<PPPMapPool>(JsonConvert.SerializeObject(pool))!;
                var next = new PPCalculatorBeatLeader<MockBeatLeader>(new() { [restored.Id] = restored }, settings);
                try
                {
                    Assert.AreEqual(42, restored.BeatLeaderFormulaRevision);
                    Assert.IsTrue(restored.BeatLeaderSessionRebasePending);
                    Assert.IsFalse(next.HasScoreDataForPPGain(restored));
                    Assert.AreEqual(200d, restored.ScoreSync.ExpectedScores.Values.Single().pp,
                        "New PPv4.2 expectations must not be weakened on every restart.");
                }
                finally { next.Dispose(); }
            }
            finally { calculator.Dispose(); }
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void V42ImproveTargetReachesOneWeightedPp(bool existingScore)
        {
            var pool = Pool();
            pool.BeatLeaderFormulaRevision = 42;
            var calculator = new PPCalculatorBeatLeader<MockBeatLeader>(new() { [pool.Id] = pool }, Settings());
            try
            {
                var map = new PPPBeatMapInfo(new PPPBeatMapInfo(), new PPPStarRating(1, 6, 5, 9, true)
                    { PredictedAcc = 0.975, SliderPercentage = 0.5, Notes = 100, NoteSpan = 40 });
                var curve = new BeatLeaderPPPCurve();
                string key = existingScore ? "OLD_SOLOSTANDARD_9" : "NEW_SOLOSTANDARD_9";
                double Gain(double pp) => calculator.GetPlayerScorePPGain(key, pp, pool).PpGainWeighted;
                double PP(double percentage) => curve.CalculatePPatPercentage(map, percentage, false, false);
                double raw = ImproveTargetSearch.FindRawPP(Gain, 1, curve.CalculateMaxPP(map))!.Value;
                double percentage = ImproveTargetSearch.FindDisplayedPercentage(PP, raw)!.Value;
                Assert.IsTrue(Gain(PP(percentage)) >= 1);
                Assert.IsTrue(Gain(PP(percentage - 0.01)) < 1);
            }
            finally { calculator.Dispose(); }
        }
    }
}
