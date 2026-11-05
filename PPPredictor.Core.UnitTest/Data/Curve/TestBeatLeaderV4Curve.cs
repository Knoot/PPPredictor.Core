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
        // PPv4.2 reference values: beatleader-website 38b04d3, executed independently of this port.
        [DataTestMethod]
        [DataRow(0.95d, 0d, 500d, 120d, 5d, 7d, 4d, 1d, new double[] { 92.12381056292533d, 265.9920248292983d, 308.3681413528089d, 373.03125245634584d, 635.1320916220951d, 1517.2214193689852d }, 208.8860700350321d)]
        [DataRow(0.962d, 0.2d, 400d, 100d, 8d, 9d, 6d, 1d, new double[] { 126.04481904744286d, 352.0033824871693d, 429.670978825794d, 516.3259823301335d, 843.7399862959713d, 1760.0474514671796d }, 301.39765688756376d)]
        [DataRow(0.975d, 0.5d, 100d, 40d, 6d, 5d, 9d, 1d, new double[] { 106.12207425112267d, 255.35817472611805d, 293.27831170300044d, 387.6177427406988d, 657.5213649904163d, 835.8988798414746d }, 253.61204483703085d)]
        [DataRow(0.99d, 1d, 20d, 12d, 2d, 1d, 2d, 1d, new double[] { 23.134857287040127d, 64.5458403970571d, 74.02946306089076d, 91.1317143921957d, 227.58988775446392d, 467.362744356922d }, 92.1860797953679d)]
        [DataRow(0d, 0d, 700d, 150d, 15d, 18d, 16d, 1d, new double[] { 285.34853389283285d, 684.0299688492438d, 763.967317196006d, 902.867072977672d, 1483.8666438092887d, 3317.592945264991d }, 594.3670802840059d)]
        [DataRow(0.985d, 0.3d, 200d, 80d, 1d, 1d, 1d, 0.8d, new double[] { 11.390298787481973d, 40.30693926956881d, 46.92920434135634d, 60.58813951080385d, 121.14250180840887d, 195.78055242001764d }, 37.71446364124936d)]
        [DataRow(0.985d, 0.3d, 200d, 80d, 4d, 4d, 4d, 1.1d, new double[] { 76.41722213214912d, 206.13334035955822d, 238.29389202673246d, 300.27722943028976d, 562.4440564128695d, 879.4072537834677d }, 192.82255025807353d)]
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
                "{\"status\":3,\"ppVersion\":2,\"predictedAcc\":0.975,\"sliderPercentage\":0.4,\"notes\":90,\"noteSpan\":40,\"accRating\":6,\"passRating\":5,\"techRating\":9}");
            var rating = JsonConvert.DeserializeObject<PPPStarRating>(JsonConvert.SerializeObject(new PPPStarRating(difficulty)));
            Assert.IsNotNull(rating);
            Assert.AreEqual(0.975, rating.PredictedAcc);
            Assert.AreEqual(0.4, rating.SliderPercentage);
            Assert.AreEqual(90, rating.Notes);
            Assert.AreEqual(40d, rating.NoteSpan);
            Assert.AreEqual(2, rating.BeatLeaderServerPPVersion);
            Assert.AreEqual(2, rating.Copy().BeatLeaderServerPPVersion);
            var newer = rating.Copy();
            newer.BeatLeaderServerPPVersion = 3;
            Assert.IsFalse(rating.HasSameRatings(newer));
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
        public void MatchesPinnedV42ReferenceForHistoricalMapInputs()
        {
            var rating = new PPPStarRating(1, 11.075725, 4.5458593, 10.603737, true)
            {
                PredictedAcc = 0.9717675, SliderPercentage = 0.12646365, Notes = 1195, NoteSpan = 236.53848
            };
            var map = new PPPBeatMapInfo(new PPPBeatMapInfo(), rating);
            // Historical inputs, not a live score assertion: expected value from pinned PPv4.2 JS.
            Assert.AreEqual(388.85331208128076, new BeatLeaderPPPCurve().CalculatePPatPercentage(map, 92.12474, false, false), 1e-8);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void MissingSpeedSlidersFallBackButExplicitZeroIsPreserved(bool explicitZero)
        {
            var rating = new PPPStarRating(1, 7, 8, 9, true)
            {
                PredictedAcc = 0.98, SliderPercentage = 0.3, Notes = 500, NoteSpan = 120,
                BeatLeaderServerPPVersion = 2,
                ModifiersRating = new Dictionary<string, double>
                {
                    ["fsAccRating"] = 6, ["fsPassRating"] = 5, ["fsTechRating"] = 9,
                    ["fsPredictedAcc"] = 0.975
                }
            };
            if (explicitZero) rating.ModifiersRating["fsSliderPercentage"] = 0;
            var calculator = new PPCalculatorBeatLeader<MockBeatLeader>(null, null!);
            try
            {
                var result = calculator.ApplyModifiersToBeatmapInfo(new PPPBeatMapInfo(new PPPBeatMapInfo(), rating),
                    new PPPMapPool(), new GameplayModifiers { songSpeed = GameplayModifiers.SongSpeed.Faster }, false, false);
                Assert.AreEqual(explicitZero ? 0 : 0.3, result.ModifiedStarRating.SliderPercentage);
                Assert.AreEqual(2, result.ModifiedStarRating.BeatLeaderServerPPVersion);
                Assert.AreEqual(100d, result.ModifiedStarRating.NoteSpan);
            }
            finally { calculator.Dispose(); }
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
            Assert.AreEqual(1, pool.LsScores.Count, "Keep the old snapshot for recovery, not for gain calculations.");
            Assert.AreEqual(0, pool.LsLeaderboadInfo.Count);
            Assert.IsFalse(pool.HasScoreDataFetched);
            Assert.IsTrue(pool.ScoresRequireRefresh);
            Assert.IsTrue(pool.BeatLeaderSessionRebasePending);
            Assert.AreEqual(42, pool.BeatLeaderFormulaRevision);
            Assert.IsNull(pool.SessionPlayer);
            pool.LsScores.Clear();
            pool.LsScores.Add(new ShortScore("new", 300));
            pool.ScoresRequireRefresh = false;
            pool.SessionPlayer = new PPPPlayer { Pp = 100 };
            _ = new PPCalculatorBeatLeader<MockBeatLeader>(pools, null!);
            Assert.AreEqual(1, pool.LsScores.Count);
            Assert.IsTrue(pool.HasScoreDataFetched);
            Assert.IsFalse(pool.ScoresRequireRefresh);
            Assert.AreEqual(100d, pool.SessionPlayer.Pp);
        }

        [TestMethod]
        public void MatchesPinnedV42BoundaryFixtures()
        {
            using var stream = typeof(TestBeatLeaderV4Curve).Assembly.GetManifestResourceStream(
                "PPPredictor.Core.UnitTest.Data.Curve.BeatLeaderV42Fixtures.json")!;
            using var reader = new StreamReader(stream);
            var fixture = JsonConvert.DeserializeObject<ReferenceFixture>(reader.ReadToEnd())!;
            Assert.IsTrue(fixture.Source.Contains("38b04d3c39570c1dcca6a9fca5cafbdf33ca5e49"));
            Assert.IsTrue(fixture.Cases.Count >= 100);
            var curve = new BeatLeaderPPPCurve();
            foreach (var item in fixture.Cases)
            {
                var rating = new PPPStarRating(item.Multiplier, item.Acc, item.Pass, item.Tech, true)
                {
                    PredictedAcc = item.Prediction, SliderPercentage = item.Sliders, Notes = item.Notes, NoteSpan = item.Span
                };
                var map = new PPPBeatMapInfo(new PPPBeatMapInfo(), rating);
                double percentage = (item.Golf ? 1 - item.Accuracy : item.Accuracy) * 100;
                Assert.AreEqual(item.Expected, curve.CalculatePPatPercentage(map, percentage, false, false,
                    item.Golf ? LeaderboardContext.BeatLeaderGolf : LeaderboardContext.BeatLeaderDefault), 1e-7,
                    $"prediction={item.Prediction}; acc={item.Acc}; accuracy={item.Accuracy}; notes={item.Notes}; span={item.Span}");
            }
        }

        [TestMethod]
        public void ProductionRatingSnapshotMatchesV42AndMismatchedStarsBlockPrediction()
        {
            // Public API snapshot, Standard/ExpertPlus, 2026-11-05. Not fetched during tests.
            var difficulty = new BeatLeaderDifficulty
            {
                status = 3, ppVersion = 2, stars = 10.143955, accRating = 12.242393,
                passRating = 4.484648, techRating = 9.971189, predictedAcc = 0.9717675,
                notes = 1195, noteSpan = 236.53848, sliderPercentage = 0.12646364
            };
            var rating = new PPPStarRating(difficulty);
            Assert.IsTrue(new BeatLeaderV4Curve(rating).MatchesReportedStars(rating.Stars));
            var calculator = new PPCalculatorBeatLeader<MockBeatLeader>(null, null!);
            try
            {
                var pool = new PPPMapPool();
                var curve = new BeatLeaderPPPCurve();
                var valid = calculator.ApplyModifiersToBeatmapInfo(new PPPBeatMapInfo(new PPPBeatMapInfo(), rating),
                    pool, new GameplayModifiers(), false, false);
                Assert.AreEqual(PPCalculationBlockReason.None, valid.CalculationBlockReason);
                Assert.IsTrue(curve.CalculateMaxPP(valid) > 0);
                difficulty.stars = 8;
                var stale = new PPPBeatMapInfo(new PPPBeatMapInfo(), new PPPStarRating(difficulty));
                Assert.AreEqual(0d, curve.CalculateMaxPP(stale), "Direct callers must not bypass the ratings check.");
                stale = calculator.ApplyModifiersToBeatmapInfo(stale, pool, new GameplayModifiers(), false, false);
                Assert.AreEqual(PPCalculationBlockReason.IncompatibleBeatLeaderRatings, stale.CalculationBlockReason);
                Assert.AreEqual(0d, curve.CalculateMaxPP(stale));
            }
            finally { calculator.Dispose(); }
        }

        private sealed class ReferenceFixture
        {
            public string Source { get; set; } = "";
            public List<ReferenceCase> Cases { get; set; } = new();
        }

        private sealed class ReferenceCase
        {
            public double Prediction { get; set; }
            public double Sliders { get; set; }
            public double Span { get; set; }
            public double Acc { get; set; }
            public double Pass { get; set; }
            public double Tech { get; set; }
            public double Multiplier { get; set; }
            public double Accuracy { get; set; }
            public double Expected { get; set; }
            public int Notes { get; set; }
            public bool Golf { get; set; }
        }
    }
}
