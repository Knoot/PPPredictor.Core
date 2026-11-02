using PPPredictor.Core.Calculator;
using PPPredictor.Core.DataType;
using PPPredictor.Core.DataType.BeatSaberEncapsulation;
using PPPredictor.Core.DataType.Curve;
using PPPredictor.Core.DataType.MapPool;
using PPPredictor.Core.UnitTest.MockService;
using static PPPredictor.Core.DataType.Enums;

namespace UnitTests.Data.Curve
{
    [TestClass]
    public class TestBeatLeaderContextRestrictions
    {
        private static PPPBeatMapInfo RankedMap(bool ppv4 = true) => new PPPBeatMapInfo(new PPPBeatMapInfo(),
            new PPPStarRating(1, 8, 7, 6, true)
            {
                PredictedAcc = 0.975,
                Notes = 500,
                NoteSpan = ppv4 ? 120 : null,
                ModifierValues = new Dictionary<string, double> { ["sc"] = -0.5, ["pm"] = 0, ["nb"] = -0.2 }
            });

        [DataTestMethod]
        [DataRow(false, false, false)]
        [DataRow(false, true, false)]
        [DataRow(true, false, false)]
        [DataRow(true, true, true)]
        public void ScpmRequiresBothModifiers(bool smallCubes, bool proMode, bool allowed)
        {
            var calculator = new PPCalculatorBeatLeader<MockBeatLeader>(null, null!);
            var pool = new PPPMapPool { LeaderboardContext = LeaderboardContext.BeatLeaderSCPM };
            var map = calculator.ApplyModifiersToBeatmapInfo(RankedMap(), pool,
                new GameplayModifiers { smallCubes = smallCubes, proMode = proMode }, false, false);

            Assert.AreEqual(allowed ? PPCalculationBlockReason.None : PPCalculationBlockReason.RequiresSmallCubesAndProMode,
                map.CalculationBlockReason);
            double pp = new BeatLeaderPPPCurve().CalculatePPatPercentage(map, 96, false, false, pool.LeaderboardContext);
            if (allowed) Assert.IsTrue(pp > 0);
            else Assert.AreEqual(0d, pp);
        }

        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void SwitchingToGeneralRestoresCalculationWithoutClearingCache(bool ppv4)
        {
            var calculator = new PPCalculatorBeatLeader<MockBeatLeader>(null, null!);
            var pool = new PPPMapPool { LeaderboardContext = LeaderboardContext.BeatLeaderSCPM };
            var modifiers = new GameplayModifiers();
            var map = calculator.ApplyModifiersToBeatmapInfo(RankedMap(ppv4), pool, modifiers, false, false);
            var curve = new BeatLeaderPPPCurve();
            Assert.AreEqual(0d, curve.CalculateMaxPP(map, pool.LeaderboardContext));
            Assert.AreEqual(PPCalculationBlockReason.RequiresSmallCubesAndProMode, map.CalculationBlockReason);

            pool.LeaderboardContext = LeaderboardContext.BeatLeaderDefault;
            map = calculator.ApplyModifiersToBeatmapInfo(map, pool, modifiers, false, false);
            Assert.AreEqual(PPCalculationBlockReason.None, map.CalculationBlockReason);
            Assert.IsTrue(curve.CalculateMaxPP(map, pool.LeaderboardContext) > 0);
            Assert.IsTrue(curve.CalculatePPatPercentage(map, 96, false, false, pool.LeaderboardContext) > 0);
        }

        [DataTestMethod]
        [DataRow(LeaderboardContext.BeatLeaderNoModifiers, PPCalculationBlockReason.ModifiersNotAllowed)]
        [DataRow(LeaderboardContext.BeatLeaderGolf, PPCalculationBlockReason.NegativeModifiersNotAllowed)]
        public void ForbiddenModifiersHaveExplicitReason(LeaderboardContext context, PPCalculationBlockReason reason)
        {
            var calculator = new PPCalculatorBeatLeader<MockBeatLeader>(null, null!);
            var pool = new PPPMapPool { LeaderboardContext = context };
            var map = calculator.ApplyModifiersToBeatmapInfo(RankedMap(), pool,
                new GameplayModifiers { noBombs = true }, false, false);
            Assert.AreEqual(reason, map.CalculationBlockReason);
            Assert.AreEqual(0d, new BeatLeaderPPPCurve().CalculatePPatPercentage(map, 40, false, false, context));

            map = calculator.ApplyModifiersToBeatmapInfo(map, pool, new GameplayModifiers(), false, false);
            Assert.AreEqual(PPCalculationBlockReason.None, map.CalculationBlockReason);
            Assert.IsTrue(new BeatLeaderPPPCurve().CalculatePPatPercentage(map, 40, false, false, context) > 0);
        }
    }
}
