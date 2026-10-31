using Newtonsoft.Json;
using PPPredictor.Core.Calculator;
using PPPredictor.Core.DataType;
using PPPredictor.Core.DataType.BeatSaberEncapsulation;
using PPPredictor.Core.DataType.Curve;
using PPPredictor.Core.DataType.MapPool;
using PPPredictor.Core.DataType.Score;
using PPPredictor.Core.UnitTest.MockService;
using static PPPredictor.Core.DataType.Enums;
using static PPPredictor.Core.DataType.LeaderBoard.BeatLeaderDataTypes;

namespace UnitTests.Data.Curve
{
    [TestClass]
    public class TestBeatLeaderV4Curve
    {
        // Expected values produced by the official rebalanced-pp.js, not this port.
        [DataTestMethod]
        [DataRow(0.95d, 0d, 500d, 120d, 5d, 7d, 4d, 1d, new double[] { 74.90256915170096d, 226.28203865080965d, 255.49781628009168d, 307.206803934506d, 531.768824275541d, 1355.9164778593456d }, 217.04834491791553d)]
        [DataRow(0.962d, 0.2d, 400d, 100d, 8d, 9d, 6d, 1d, new double[] { 108.32310392686108d, 349.76633094567194d, 407.8069760009841d, 485.9422564178345d, 794.4979446636723d, 1789.5888164052412d }, 322.75971333959177d)]
        [DataRow(0.975d, 0.5d, 100d, 40d, 6d, 5d, 9d, 1d, new double[] { 65.45443717100136d, 259.5351138153983d, 291.5747964249401d, 372.47344979036995d, 662.655732126939d, 1091.4964335503057d }, 250.18805306009241d)]
        [DataRow(0.99d, 1d, 20d, 12d, 2d, 1d, 2d, 1d, new double[] { 5.958647356923199d, 44.70395973872188d, 51.84177980979372d, 64.21058466819093d, 158.24161494355283d, 454.136832532058d }, 71.62248195788054d)]
        [DataRow(0d, 0d, 700d, 150d, 15d, 18d, 16d, 1d, new double[] { 237.76746003701243d, 789.5921335612305d, 885.6804482275264d, 1049.7390092856576d, 1715.7199462903586d, 3900.0724348118974d }, 614.185757173784d)]
        [DataRow(0.985d, 0.3d, 200d, 80d, 1d, 1d, 1d, 0.8d, new double[] { 2.6426559442280886d, 19.462093899975706d, 22.727215237830983d, 29.307865177995982d, 58.645082509894195d, 107.17723875702656d }, 28.3852406024037d)]
        [DataRow(0.985d, 0.3d, 200d, 80d, 4d, 4d, 4d, 1.1d, new double[] { 48.670761334227194d, 178.65032343484813d, 203.7247343133209d, 254.12220978537863d, 478.4908819789546d, 849.5694223027003d }, 182.84931716354566d)]
        public void MatchesOfficialFormula(double prediction, double sliders, double notes, double span,
            double acc, double pass, double tech, double multiplier, double[] expected, double golf)
        {
            var rating = new PPPStarRating(multiplier, acc, pass, tech, true)
            {
                PredictedAcc = prediction, SliderPercentage = sliders, Notes = (int)notes, NoteSpan = span
            };
            var map = new PPPBeatMapInfo(new PPPBeatMapInfo(), rating);
            var curve = new BeatLeaderPPPCurve();
            double[] accuracies = { 30, 94, 96, 97.5, 99, 100 };
            for (int i = 0; i < accuracies.Length; i++)
                Assert.AreEqual(expected[i], curve.CalculatePPatPercentage(map, accuracies[i], false, false), 1e-8);
            Assert.AreEqual(golf, curve.CalculatePPatPercentage(map, 30, false, false, LeaderboardContext.BeatLeaderGolf), 1e-8);
            Assert.AreEqual(expected[5], curve.CalculateMaxPP(map), 1e-8);
            Assert.AreEqual(0d, curve.CalculatePPatPercentage(map, 96, true, false));
            Assert.AreEqual(0d, curve.CalculatePPatPercentage(map, 96, false, true, LeaderboardContext.BeatLeaderNoPauses));
        }

        [TestMethod]
        public void ApiFieldsSurviveCacheRoundTrip()
        {
            var difficulty = JsonConvert.DeserializeObject<BeatLeaderDifficulty>(
                "{\"status\":3,\"predictedAcc\":0.975,\"sliderPercentage\":0.4,\"notes\":90,\"noteSpan\":40,\"accRating\":6,\"passRating\":5,\"techRating\":9}");
            var rating = JsonConvert.DeserializeObject<PPPStarRating>(JsonConvert.SerializeObject(new PPPStarRating(difficulty)));
            Assert.IsNotNull(rating);
            Assert.AreEqual(0.975, rating.PredictedAcc);
            Assert.AreEqual(0.4, rating.SliderPercentage);
            Assert.AreEqual(90, rating.Notes);
            Assert.AreEqual(40d, rating.NoteSpan);
            Assert.IsNull(new PPPStarRating(new BeatLeaderDifficulty()).NoteSpan);
            Assert.AreEqual(0d, new PPPStarRating(new BeatLeaderDifficulty { noteSpan = 0 }).NoteSpan);
        }

        [DataTestMethod]
        [DataRow(GameplayModifiers.SongSpeed.Slower, "ss", 0.85)]
        [DataRow(GameplayModifiers.SongSpeed.Faster, "fs", 1.2)]
        [DataRow(GameplayModifiers.SongSpeed.SuperFast, "sf", 1.5)]
        public void SpeedModifiersPreserveCurveInputs(GameplayModifiers.SongSpeed speed, string prefix, double timescale)
        {
            var calc = new PPCalculatorBeatLeader<MockBeatLeader>(null, null!);
            var pool = new PPPMapPool { LeaderboardContext = LeaderboardContext.BeatLeaderDefault };
            var rating = new PPPStarRating(1, 7, 8, 9, true)
            {
                PredictedAcc = 0.98, SliderPercentage = 0.1, Notes = 100, NoteSpan = 60,
                ModifiersRating = new Dictionary<string, double>
                {
                    [prefix + "AccRating"] = 6, [prefix + "PassRating"] = 5, [prefix + "TechRating"] = 9,
                    [prefix + "PredictedAcc"] = 0.975, [prefix + "SliderPercentage"] = 0.5
                }
            };
            var map = new PPPBeatMapInfo(new PPPBeatMapInfo(), rating);
            map = calc.ApplyModifiersToBeatmapInfo(map, pool, new GameplayModifiers { songSpeed = speed }, false, false);
            var modified = map.ModifiedStarRating;
            Assert.AreEqual(0.975, modified.PredictedAcc);
            Assert.AreEqual(0.5, modified.SliderPercentage);
            Assert.AreEqual(100, modified.Notes);
            Assert.AreEqual(60 / timescale, modified.NoteSpan);
            Assert.AreEqual(1d, modified.Multiplier);
            Assert.AreEqual(60d, rating.NoteSpan);
            var expected = new PPPStarRating(1, 6, 5, 9, true)
            {
                PredictedAcc = 0.975, SliderPercentage = 0.5, Notes = 100, NoteSpan = 60 / timescale
            };
            Assert.AreEqual(new BeatLeaderV4Curve(expected).Calculate(0.975, expected, false),
                new BeatLeaderPPPCurve().CalculatePPatPercentage(map, 97.5, false, false), 1e-8);
        }

        [TestMethod]
        public void MatchesLiveRankedScore()
        {
            var rating = new PPPStarRating(1, 11.075725, 4.5458593, 10.603737, true)
            {
                PredictedAcc = 0.9717675, SliderPercentage = 0.12646365, Notes = 1195, NoteSpan = 236.53848
            };
            var map = new PPPBeatMapInfo(new PPPBeatMapInfo(), rating);
            // Public BeatLeader score on 3eb725a7d0343654527475369ae950a6ab8154f7, Expert+.
            Assert.AreEqual(440.5711, new BeatLeaderPPPCurve().CalculatePPatPercentage(map, 92.12474, false, false), 0.001);
        }

        [TestMethod]
        public void MigratesOldBeatLeaderCacheOnlyOnce()
        {
            var pool = new PPPMapPool { SessionPlayer = new PPPPlayer() };
            pool.LsScores.Add(new ShortScore("old", 500));
            pool.LsLeaderboadInfo.Add(new ShortScore("old", 500));
            var pools = new Dictionary<string, PPPMapPool> { ["-1"] = pool };
            _ = new PPCalculatorBeatLeader<MockBeatLeader>(pools, null!);
            Assert.AreEqual(4, pool.BeatLeaderPPVersion);
            Assert.AreEqual(0, pool.LsScores.Count);
            Assert.AreEqual(0, pool.LsLeaderboadInfo.Count);
            Assert.IsFalse(pool.HasScoreDataFetched);
            Assert.IsNull(pool.SessionPlayer);
            pool.LsScores.Add(new ShortScore("new", 300));
            _ = new PPCalculatorBeatLeader<MockBeatLeader>(pools, null!);
            Assert.AreEqual(1, pool.LsScores.Count);
            Assert.IsTrue(pool.HasScoreDataFetched);
        }
    }
}
