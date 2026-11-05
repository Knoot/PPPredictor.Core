using PPPredictor.Core;
using PPPredictor.Core.Calculator;
using PPPredictor.Core.DataType;
using PPPredictor.Core.DataType.MapPool;
using PPPredictor.Core.DataType.Score;
using PPPredictor.Core.UnitTest.MockService;
using static PPPredictor.Core.DataType.Enums;

namespace UnitTests.Data
{
    [TestClass]
    public class TestImproveTargetSearch
    {
        private static PPCalculatorScoreSaber<MockScoreSaberApi> Calculator(PPPMapPool pool) => new(new() { [pool.Id] = pool },
            new Settings(true, false, false, false, "123", PPGainCalculationType.Weighted, MapPoolSorting.Alphabetical,
                "", -7, DateTime.Now, 12), _ => new PPPBeatMapInfo());

        [DataTestMethod]
        [DataRow(0.001)]
        [DataRow(0.015)]
        [DataRow(0.995)]
        public void WeightedGainAndTotalKeepUnroundedValues(double delta)
        {
            var pool = new PPPMapPool { AccumulationConstant = 0.965f, CurrentPlayer = new PPPPlayer { Pp = 100 } };
            pool.LsScores.Add(new ShortScore("TARGET", 100));
            var calculator = Calculator(pool);
            var gain = calculator.GetPlayerScorePPGain("TARGET", 100 + delta, pool);
            Assert.AreEqual(delta, gain.PpGainWeighted, 1e-10);
            Assert.AreEqual(100 + delta, gain.PpTotal, 1e-10);
        }

        [DataTestMethod]
        [DataRow("TARGET", 1d)]
        [DataRow("NEW", 1d)]
        [DataRow("TARGET", 10d)]
        [DataRow("NEW", 10d)]
        public void RawTargetReachesSelectedGainAgainstIndependentWeightedSum(string map, double weightedTarget)
        {
            var pool = new PPPMapPool { AccumulationConstant = 0.965f, CurrentPlayer = new PPPPlayer { Pp = 1000 } };
            pool.LsScores.AddRange(new[] { new ShortScore("TOP", 300), new ShortScore("TARGET", 250), new ShortScore("LOW", 200) });
            var calculator = Calculator(pool);
            double Gain(double raw) => calculator.GetPlayerScorePPGain(map, raw, pool).PpGainWeighted;
            var target = ImproveTargetSearch.FindRawPP(Gain, weightedTarget, 1000);
            Assert.IsNotNull(target);
            double Total(IEnumerable<double> scores) => scores.OrderByDescending(x => x)
                .Select((pp, index) => pp * Math.Pow(pool.AccumulationConstant, index)).Sum();
            double independent = Total(pool.LsScores.Where(x => x.Searchstring != map).Select(x => x.Pp).Append(target.Value))
                - Total(pool.LsScores.Select(x => x.Pp));
            Assert.AreEqual(weightedTarget, independent, 1e-8);
            Assert.IsTrue(Gain(target.Value) >= weightedTarget);
            Assert.IsTrue(Gain(target.Value - 0.000001) < weightedTarget);
            Assert.IsTrue(Gain(target.Value - 0.001) > weightedTarget - 0.01);
        }

        [TestMethod]
        public void UnreachableAndNonfiniteGainsDoNotProduceTargets()
        {
            Assert.IsNull(ImproveTargetSearch.FindRawPP(_ => 0.999, 1, 1000));
            Assert.IsNull(ImproveTargetSearch.FindRawPP(_ => double.NaN, 1, 1000));
            Assert.IsNull(ImproveTargetSearch.FindRawPP(_ => double.PositiveInfinity, 1, 1000));
            Assert.IsNull(ImproveTargetSearch.FindRawPP(x => x, 1, double.NaN));
        }

        [DataTestMethod]
        [DataRow(95.551, 95.56)]
        [DataRow(95.55, 95.55)]
        [DataRow(99.99999, 100)]
        [DataRow(0, 0)]
        [DataRow(100, 100)]
        public void DisplayedPercentageNeverRoundsBelowTarget(double threshold, double expected)
        {
            var percentage = ImproveTargetSearch.FindDisplayedPercentage(x => x, threshold);
            Assert.IsNotNull(percentage);
            Assert.AreEqual(expected, percentage);
            Assert.IsTrue(percentage.Value >= threshold);
            if (percentage > 0) Assert.IsTrue(percentage.Value - 0.01 < threshold);
        }

        [TestMethod]
        public void AccuracyTargetHandlesNonmonotoneAndUnavailableCurves()
        {
            Assert.AreEqual(50d, ImproveTargetSearch.FindDisplayedPercentage(p => p <= 50 ? p * 2 : 0, 99.999));
            Assert.IsNull(ImproveTargetSearch.FindDisplayedPercentage(_ => 0, 1));
            Assert.IsNull(ImproveTargetSearch.FindDisplayedPercentage(_ => double.NaN, 1));
            Assert.AreEqual(95.56, ImproveTargetSearch.FindDisplayedPercentage(p => p, Math.BitIncrement(95.55)));
        }
    }
}
