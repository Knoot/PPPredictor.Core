using PPPredictor.Core;
using PPPredictor.Core.Calculator;
using PPPredictor.Core.DataType;
using PPPredictor.Core.DataType.Curve;
using PPPredictor.Core.DataType.MapPool;
using PPPredictor.Core.DataType.Score;
using PPPredictor.Core.Interface;
using static PPPredictor.Core.DataType.Enums;
using static PPPredictor.Core.DataType.LeaderBoard.AccSaberDataTypes;

namespace UnitTests.LeaderBoard
{
    [TestClass]
    public class TestAccSaber
    {
        internal sealed class Api : IAccSaberAPI
        {
            internal List<AccSaberScores> Scores = new();
            internal bool Fail;
            internal Func<string, AccSaberMap> Map = _ => new AccSaberMap();
            public Task<List<AccSaberScores>> GetAllScores(string userId) => Fail
                ? throw new InvalidOperationException("offline") : Task.FromResult(Scores);
            public Task<List<AccSaberScores>> GetAllScoresByPool(string userId, string poolId) => Fail
                ? throw new InvalidOperationException("offline") : Task.FromResult(Scores.Where(x => x.categoryDisplayName == poolId).ToList());
            public Task<AccSaberPlayer> GetAccSaberUserByPool(long userId, string poolIdent) => Task.FromResult(new AccSaberPlayer());
            public Task<List<AccSaberRankedMap>> GetAllRankedMaps() => Task.FromResult(new List<AccSaberRankedMap>());
            public Task<List<AccSaberRankedMap>> GetRankedMaps(string pool) => GetAllRankedMaps();
            public Task<AccSaberMap> GetMapByHash(string hash) => Task.FromResult(Map(hash));
            public Task<List<AccSaberMapPool>> GetAccSaberMapPools() => Task.FromResult(new List<AccSaberMapPool>());
            public Task<List<AccSaberPlayer>> GetPlayerListForMapPool(double page, string pool) => Task.FromResult(new List<AccSaberPlayer>());
        }

        private const string Target = "TARGET_SOLOSTANDARD_9";
        private static Settings Settings() => new Settings(false, false, false, true, "123", PPGainCalculationType.Weighted,
            MapPoolSorting.Alphabetical, "", -7, DateTime.Now.AddHours(-1), 12);
        private static PPPMapPool Pool(string id = "overall", string category = "tech")
        {
            var pool = new PPPMapPool(id, id == "overall" ? MapPoolType.Default : MapPoolType.Custom, id, 0, 0,
                CurveParser.ParseToCurve(new CurveInfo(CurveType.AccSaber)));
            pool.LsLeaderboadInfo.Add(new ShortScore(Target, new PPPStarRating(5), DateTime.Now, category));
            return pool;
        }
        private static PPCalculatorAccSaber<Api> Calculator(PPPMapPool pool, Api api) =>
            new(new Dictionary<string, PPPMapPool> { [pool.Id] = pool }, Settings(), api);
        private static AccSaberScores Score(string hash, double ap, string category, long score = 1000) => new()
        {
            songHash = hash, ap = ap, categoryDisplayName = category, difficulty = "EXPERT_PLUS",
            timeSet = DateTime.UtcNow, weightedAp = 1, scoreNoMods = score
        };

        private static double Total(IEnumerable<AccSaberScores> scores)
        {
            double x0 = -Math.Log(0.9 / (0.1 * Math.Exp(0.4 * 15) - 1)) / 0.4;
            return scores.GroupBy(x => x.categoryDisplayName).Sum(category => category
                .GroupBy(x => (x.songHash, x.difficulty)).Select(x => x.Max(s => s.ap)).OrderByDescending(x => x)
                .Select((ap, index) => ap * (1 + Math.Exp(-0.4 * x0)) / (1 + Math.Exp(0.4 * (index - x0)))).Sum());
        }

        [DataTestMethod]
        [DataRow("TARGET", 350d)]
        [DataRow("NEW", 250d)]
        public async Task OverallGainMatchesIndependentCategoryRecalculation(string hash, double candidate)
        {
            var scores = new List<AccSaberScores>
            {
                Score("TRUE1", 900, "true"), Score("STANDARD1", 800, "standard"),
                Score("TECH1", 400, "tech"), Score("TARGET", 200, "tech"), Score("TECH2", 150, "tech")
            };
            var pool = Pool();
            pool.CurrentPlayer.Pp = Total(scores) + 123;
            pool.LsLeaderboadInfo.Add(new ShortScore("NEW_SOLOSTANDARD_9", new PPPStarRating(5), DateTime.Now, "tech"));
            var calculator = Calculator(pool, new Api { Scores = scores });
            Assert.IsTrue(await calculator.GetPlayerScores(pool, 10, 100));
            var gain = calculator.GetPlayerScorePPGain(hash + "_SOLOSTANDARD_9", candidate, pool);
            var replacement = scores.Where(x => x.songHash != hash).Append(Score(hash, candidate, "tech"));
            double expected = Math.Round(Total(replacement) - Total(scores), 2, MidpointRounding.AwayFromZero);
            Assert.AreEqual(expected, gain.PpGainWeighted, 1e-8);
            Assert.AreEqual(Math.Round(pool.CurrentPlayer.Pp + expected, 2, MidpointRounding.AwayFromZero), gain.PpTotal, 1e-8);
        }

        [DataTestMethod]
        [DataRow(200d)]
        [DataRow(150d)]
        public async Task ReplayingExistingOverallScoreDoesNotAddItAgain(double candidate)
        {
            var pool = Pool();
            var calculator = Calculator(pool, new Api { Scores = new() { Score("OTHER", 900, "true"), Score("TARGET", 200, "tech") } });
            await calculator.GetPlayerScores(pool, 10, 100);
            var gain = calculator.GetPlayerScorePPGain(Target, candidate, pool);
            Assert.AreEqual(0d, gain.PpGainWeighted);
            Assert.AreEqual(candidate - 200, gain.PpGainRaw);
        }

        [TestMethod]
        public async Task FirstScoreInEmptyCategoryDoesNotUseOtherCategoriesWeights()
        {
            var pool = Pool(category: "true");
            var calculator = Calculator(pool, new Api { Scores = new() { Score("OTHER", 900, "tech") } });
            await calculator.GetPlayerScores(pool, 10, 100);
            Assert.IsTrue(calculator.HasScoreDataForPPGain(pool));
            Assert.AreEqual(100d, calculator.GetPlayerScorePPGain(Target, 100, pool).PpGainWeighted);
        }

        [DataTestMethod]
        [DataRow("overall")]
        [DataRow("tech")]
        public async Task SuccessfulEmptyProfileIsReadyButFailedFirstLoadIsNot(string id)
        {
            var pool = Pool(id);
            var api = new Api { Fail = true };
            var calculator = Calculator(pool, api);
            Assert.IsFalse(await calculator.GetPlayerScores(pool, 10, 100));
            Assert.IsFalse(calculator.HasScoreDataForPPGain(pool));
            api.Fail = false;
            Assert.IsTrue(await calculator.GetPlayerScores(pool, 10, 100));
            Assert.IsTrue(calculator.HasScoreDataForPPGain(pool));
            Assert.AreEqual(1d, calculator.GetPlayerScorePPGain(Target, 1, pool).PpGainWeighted);
        }

        [TestMethod]
        public async Task FailedOverallRefreshKeepsPreviousCategorySnapshot()
        {
            var pool = Pool();
            var api = new Api { Scores = new() { Score("TARGET", 200, "tech") } };
            var calculator = Calculator(pool, api);
            await calculator.GetPlayerScores(pool, 10, 100);
            double before = calculator.GetPlayerScorePPGain(Target, 250, pool).PpGainWeighted;
            api.Scores = new() { Score("TARGET", 500, "unknown") };
            Assert.IsFalse(await calculator.GetPlayerScores(pool, 10, 100));
            Assert.AreEqual(before, calculator.GetPlayerScorePPGain(Target, 250, pool).PpGainWeighted);
            Assert.AreEqual(200d, pool.LsScores.Single().Pp);
        }

        [TestMethod]
        public async Task CategoryRefreshReplacesRemovedAndReweightedScores()
        {
            var pool = Pool("tech");
            var api = new Api { Scores = new() { Score("TARGET", 300, "tech"), Score("REMOVED", 100, "tech") } };
            var calculator = Calculator(pool, api);
            await calculator.GetPlayerScores(pool, 10, 100);
            api.Scores = new() { Score("TARGET", 200, "tech") };
            await calculator.GetPlayerScores(pool, 10, 100);
            Assert.AreEqual(200d, pool.LsScores.Single().Pp);
        }

        [TestMethod]
        public async Task DuplicateScoresDoNotContributeTwice()
        {
            var pool = Pool();
            var calculator = Calculator(pool, new Api { Scores = new() { Score("TARGET", 100, "tech"), Score("TARGET", 200, "tech") } });
            await calculator.GetPlayerScores(pool, 10, 100);
            Assert.AreEqual(200d, pool.LsScores.Single().Pp);
            Assert.AreEqual(50d, calculator.GetPlayerScorePPGain(Target, 250, pool).PpGainWeighted);
        }

        [TestMethod]
        public async Task SameSongOnAnotherDifficultyIsNotTheSamePersonalBest()
        {
            var otherDifficulty = Score("TARGET", 400, "tech");
            otherDifficulty.difficulty = "EXPERT";
            var scores = new List<AccSaberScores> { otherDifficulty, Score("TARGET", 200, "tech") };
            var pool = Pool();
            var calculator = Calculator(pool, new Api { Scores = scores });
            await calculator.GetPlayerScores(pool, 10, 100);
            Assert.AreEqual(2, pool.LsScores.Count);
            double expected = Math.Round(Total(new[] { otherDifficulty, Score("TARGET", 250, "tech") }) - Total(scores),
                2, MidpointRounding.AwayFromZero);
            Assert.AreEqual(expected, calculator.GetPlayerScorePPGain(Target, 250, pool).PpGainWeighted);
        }

        [TestMethod]
        public async Task CategoryDisplayNameAliasesShareOneWeightingList()
        {
            var pool = Pool(category: "Tech Acc");
            var calculator = Calculator(pool, new Api
            {
                Scores = new() { Score("OTHER", 400, "Tech Acc"), Score("TARGET", 200, "tech_acc") }
            });
            await calculator.GetPlayerScores(pool, 10, 100);
            var normalized = new[] { Score("OTHER", 400, "tech"), Score("TARGET", 200, "tech") };
            double expected = Math.Round(Total(new[] { Score("OTHER", 400, "tech"), Score("TARGET", 250, "tech") }) - Total(normalized),
                2, MidpointRounding.AwayFromZero);
            Assert.AreEqual(expected, calculator.GetPlayerScorePPGain(Target, 250, pool).PpGainWeighted);
        }

        [TestMethod]
        public async Task ConfirmationWaitsForSourceScoreWithoutComparingApToOtherLeaderboardsPp()
        {
            var pool = Pool("tech");
            var api = new Api { Scores = new() { Score("TARGET", 100, "tech", 900) } };
            var calculator = Calculator(pool, api);
            var expected = new PPPScoreSetData { hash = Target, score = 950 };
            Assert.IsFalse(await calculator.GetPlayerScores(pool, 10, 100, expectedScore: expected));
            api.Scores = new() { Score("TARGET", 110, "tech", 1000) };
            Assert.IsTrue(await calculator.GetPlayerScores(pool, 10, 100, expectedScore: expected));
        }

        [TestMethod]
        public void ScoreEventMatchesExactModeAndDifficulty()
        {
            var pool = Pool();
            var calculator = Calculator(pool, new Api());
            Assert.IsTrue(calculator.IsScoreSetOnCurrentMapPool(pool, new PPPScoreSetData { hash = Target.ToLowerInvariant() }));
            Assert.IsFalse(calculator.IsScoreSetOnCurrentMapPool(pool, new PPPScoreSetData { hash = "TARGET_SOLOSTANDARD_7" }));
            Assert.IsFalse(calculator.IsScoreSetOnCurrentMapPool(pool, new PPPScoreSetData { hash = "TARGET_SOLOONESABER_9" }));
        }
    }
}
