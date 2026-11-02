using PPPredictor.Core;
using PPPredictor.Core.Calculator;
using PPPredictor.Core.DataType;
using PPPredictor.Core.DataType.MapPool;
using PPPredictor.Core.UnitTest.MockService;
using static PPPredictor.Core.DataType.Enums;

namespace UnitTests.Data
{
    [TestClass]
    public class TestRankingCache
    {
        private sealed class Calculator : PPCalculatorScoreSaber<MockScoreSaberApi>
        {
            internal int Calls;
            internal Func<double, List<PPPPlayer>> Fetch = page => Players(page);
            internal PPPPlayer Profile = Player(50, 1000);
            internal Calculator(PPPMapPool pool) : base(new() { [pool.Id] = pool },
                new Settings(true, false, false, false, "123", PPGainCalculationType.Weighted,
                    MapPoolSorting.Alphabetical, "", -7, DateTime.Now.AddHours(-1), 12), _ => new PPPBeatMapInfo()) { }
            internal override Task<List<PPPPlayer>> GetPlayers(double page, PPPMapPool pool)
            { Calls++; return Task.FromResult(Fetch(page)); }
            internal override Task<PPPPlayer> GetPlayerInfo(long id, PPPMapPool pool) => Task.FromResult(Profile);
        }

        private static PPPPlayer Player(int rank, double pp) => new() { Rank = rank, CountryRank = rank, Country = "RU", Pp = pp };
        private static List<PPPPlayer> Players(double page, double offset = 0) => Enumerable.Range(((int)page - 1) * 50 + 1, 50)
            .Select(rank => Player(rank, 2000 - rank * 20 + offset)).ToList();
        private static PPPMapPool Pool() => new() { CurrentPlayer = Player(50, 1000) };

        [TestMethod]
        public async Task ExpiredRankingsAreRefetchedAndReadsDoNotExtendLifetime()
        {
            var now = DateTime.UtcNow;
            var pool = Pool();
            var calculator = new Calculator(pool) { RankCacheUtcNow = () => now };
            var result = await calculator.GetPlayerRankGain(1050, pool);
            Assert.AreEqual(48, result.RankGlobal);
            Assert.AreEqual(48, result.RankCountry);
            var originalTimestamp = pool.PlayerRankingsFetchedAtUtc;
            now = now.AddMinutes(4);
            await calculator.GetPlayerRankGain(1050, pool);
            Assert.AreEqual(1, calculator.Calls);
            Assert.AreEqual(originalTimestamp, pool.PlayerRankingsFetchedAtUtc);
            now = now.AddMinutes(1);
            calculator.Fetch = page => Players(page, -100);
            Assert.AreEqual(43, (await calculator.GetPlayerRankGain(1050, pool)).RankGlobal);
            Assert.AreEqual(2, calculator.Calls);
            Assert.AreEqual(now, pool.PlayerRankingsFetchedAtUtc);
        }

        [TestMethod]
        public async Task FailedRefreshKeepsSnapshotButDoesNotPresentItAsFreshPrediction()
        {
            var now = DateTime.UtcNow;
            var pool = Pool();
            var calculator = new Calculator(pool) { RankCacheUtcNow = () => now };
            await calculator.GetPlayerRankGain(1050, pool);
            var original = pool.LsPlayerRankings;
            var timestamp = pool.PlayerRankingsFetchedAtUtc;
            now = now.AddMinutes(5);
            calculator.Fetch = _ => throw new IOException("offline");
            Assert.AreEqual(-1, (await calculator.GetPlayerRankGain(1050, pool)).RankGlobal);
            Assert.AreSame(original, pool.LsPlayerRankings);
            Assert.AreEqual(timestamp, pool.PlayerRankingsFetchedAtUtc);
            Assert.AreEqual(-1, (await calculator.GetPlayerRankGain(1050, pool)).RankGlobal);
            Assert.AreEqual(2, calculator.Calls);
            now = now.AddSeconds(15);
            calculator.Fetch = page => Players(page);
            Assert.AreEqual(48, (await calculator.GetPlayerRankGain(1050, pool)).RankGlobal);
            Assert.AreEqual(3, calculator.Calls);
        }

        [TestMethod]
        public async Task FailedNextPageDoesNotCommitPartialRange()
        {
            var pool = new PPPMapPool { CurrentPlayer = Player(100, 100) };
            pool.LsPlayerRankings.Add(Player(99, 200));
            var original = pool.LsPlayerRankings;
            var calculator = new Calculator(pool) { Fetch = page => page == 2 ? Players(page) : throw new IOException("offline") };
            Assert.AreEqual(-1, (await calculator.GetPlayerRankGain(3000, pool)).RankGlobal);
            Assert.AreEqual(2, calculator.Calls);
            Assert.AreSame(original, pool.LsPlayerRankings);
            Assert.AreEqual(1, pool.LsPlayerRankings.Count);
            Assert.AreEqual(DateTime.MinValue, pool.PlayerRankingsFetchedAtUtc);
        }

        [TestMethod]
        public async Task UpdatedProfileInvalidatesRangeBeforeTtlExpires()
        {
            var pool = Pool();
            var calculator = new Calculator(pool);
            await calculator.GetPlayerRankGain(1050, pool);
            calculator.Profile = Player(49, 1020);
            await calculator.UpdatePlayer(pool, false);
            Assert.AreEqual(DateTime.MinValue, pool.PlayerRankingsFetchedAtUtc);
            await calculator.GetPlayerRankGain(1050, pool);
            Assert.AreEqual(2, calculator.Calls);
        }

        [TestMethod]
        public async Task DuplicateRanksDoNotAccumulateAndEmptyResponseIsNotSuccessfulCache()
        {
            var pool = Pool();
            var calculator = new Calculator(pool) { Fetch = page => Players(page).Concat(Players(page)).ToList() };
            await calculator.GetPlayerRankGain(1050, pool);
            Assert.AreEqual(50, pool.LsPlayerRankings.Count);
            pool.PlayerRankingsFetchedAtUtc = DateTime.MinValue;
            calculator.Fetch = _ => new List<PPPPlayer>();
            Assert.AreEqual(-1, (await calculator.GetPlayerRankGain(1050, pool)).RankGlobal);
            Assert.AreEqual(DateTime.MinValue, pool.PlayerRankingsFetchedAtUtc);
        }
    }
}
