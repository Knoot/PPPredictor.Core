using PPPredictor.Core;
using PPPredictor.Core.DataType;
using PPPredictor.Core.DataType.BeatSaberEncapsulation;
using PPPredictor.Core.DataType.Curve;
using PPPredictor.Core.DataType.LeaderBoard;
using PPPredictor.Core.DataType.MapPool;
using PPPredictor.Core.Interface;
using PPPredictor.Core.UnitTest.MockService;
using UnitTests.LeaderBoard;
using static PPPredictor.Core.DataType.Enums;

namespace UnitTests.Data
{
    [TestClass]
    public class TestGameplaySnapshots
    {
        internal sealed class HitBloqApi : MockHitBloq, IHitBloqAPI
        {
            Task<List<HitBloqDataTypes.HitBloqMapPool>> IHitBloqAPI.GetHitBloqMapPools() => Task.FromResult(new List<HitBloqDataTypes.HitBloqMapPool>());
            Task<HitBloqDataTypes.HitBloqUserId> IHitBloqAPI.GetHitBloqUserIdByUserId(string id) => Task.FromResult(new HitBloqDataTypes.HitBloqUserId { id = 123 });
        }
        [DataTestMethod]
        [DataRow(Leaderboard.ScoreSaber, "-1")]
        [DataRow(Leaderboard.BeatLeader, "-1")]
        [DataRow(Leaderboard.AccSaber, "tech")]
        [DataRow(Leaderboard.HitBloq, "test")]
        public async Task NormalAndFailedSnapshotsDoNotMutateEachOtherOrMenu(Leaderboard leaderboard, string poolId)
        {
            var settings = new Settings(leaderboard == Leaderboard.ScoreSaber, leaderboard == Leaderboard.BeatLeader,
                leaderboard == Leaderboard.HitBloq, leaderboard == Leaderboard.AccSaber, "123", PPGainCalculationType.Weighted, MapPoolSorting.Alphabetical,
                "", -7, DateTime.Now, 12);
            var data = new Dictionary<string, LeaderboardData>();
            if (leaderboard == Leaderboard.HitBloq)
                data["HitBloq"] = new LeaderboardData { DctMapPool = new()
                {
                    ["test"] = new PPPMapPool("test", MapPoolType.Custom, "Test", 0.965f, 0,
                        new CustomPPPCurve(new() { (1, 1), (0, 0) }, CurveType.Linear, 50))
                } };
            var calculator = await CalculatorInstance.CreateAsyncMock<MockScoreSaberApi, MockBeatLeader, HitBloqApi, TestAccSaber.Api>(
                settings, data, _ => new PPPBeatMapInfo());
            try
            {
                var source = new PPPBeatMapInfo(new PPPBeatMapInfo("HASH", new BeatmapKey
                    { serializedName = "Standard", difficulty = BeatMapDifficulty.ExpertPlus }), new PPPStarRating(10)
                    {
                        RankedBeatLeader = leaderboard == Leaderboard.BeatLeader ? true : null,
                        // PPv4.2 stars for these BL ratings, independently calculated by pinned upstream JS.
                        Stars = leaderboard == Leaderboard.BeatLeader ? 9.610805103965863 : 10,
                        PredictedAcc = 0.975, Notes = 500, NoteSpan = 120,
                        ModifierValues = new() { ["nf"] = -0.5 }, ModifiersRating = new() { ["test"] = 1 }
                    })
                {
                    SelectedMapSearchString = "HASH_SOLOSTANDARD_9", MaxPP = 123, MapInfoRefreshFailed = true
                };
                var modifiers = new GameplayModifiers { noFailOn0Energy = true };
                var normal = calculator.ApplyModifiersToBeatmapInfo(leaderboard, poolId, source, modifiers);
                double before = calculator.CalculatePPatPercentage(leaderboard, poolId, normal, 95, false, false);
                var failed = calculator.ApplyModifiersToBeatmapInfo(leaderboard, poolId, source, modifiers, levelFailed: true);
                Assert.AreNotSame(normal, failed);
                Assert.AreNotSame(source, normal);
                Assert.AreNotSame(normal.ModifiedStarRating, failed.ModifiedStarRating);
                Assert.AreEqual(before, calculator.CalculatePPatPercentage(leaderboard, poolId, normal, 95, false, false));
                Assert.IsTrue(before > 0);
                Assert.IsTrue(calculator.CalculatePPatPercentage(leaderboard, poolId, failed, 95, true, false) < before);
                if (leaderboard == Leaderboard.BeatLeader)
                {
                    Assert.AreEqual(1, normal.ModifiedStarRating.Multiplier);
                    Assert.AreEqual(0.5, failed.ModifiedStarRating.Multiplier);
                }
                Assert.AreEqual(123, source.MaxPP);
                Assert.AreEqual(-1, normal.MaxPP);
                Assert.IsTrue(normal.MapInfoRefreshFailed);
                Assert.AreEqual(source.SelectedMapSearchString, normal.SelectedMapSearchString);
                failed.BaseStarRating.ModifierValues["nf"] = -0.9;
                failed.BaseStarRating.ModifiersRating["test"] = 2;
                failed.BeatmapKey.difficulty = BeatMapDifficulty.Hard;
                Assert.AreEqual(-0.5, source.BaseStarRating.ModifierValues["nf"]);
                Assert.AreEqual(-0.5, normal.BaseStarRating.ModifierValues["nf"]);
                Assert.AreEqual(1, source.BaseStarRating.ModifiersRating["test"]);
                Assert.AreEqual(BeatMapDifficulty.ExpertPlus, normal.BeatmapKey.difficulty);
                Assert.AreEqual(leaderboard == Leaderboard.BeatLeader ? 9.610805103965863 : 10, source.ModifiedStarRating.Stars);
            }
            finally { await calculator.ShutdownAsync(); }
        }
    }
}
