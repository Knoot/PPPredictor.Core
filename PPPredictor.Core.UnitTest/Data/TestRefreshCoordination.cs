using PPPredictor.Core;
using PPPredictor.Core.Calculator;
using PPPredictor.Core.DataType;
using PPPredictor.Core.DataType.MapPool;
using PPPredictor.Core.DataType.Score;
using PPPredictor.Core.UnitTest.MockService;
using static PPPredictor.Core.DataType.Enums;
using static PPPredictor.Core.DataType.LeaderBoard.ScoreSaberDataTypes;

namespace UnitTests.Data
{
    [TestClass]
    public class TestRefreshCoordination
    {
        private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static Settings Settings() => new Settings(true, false, false, false, "123", PPGainCalculationType.Weighted,
            MapPoolSorting.Alphabetical, "", -7, DateTime.Now.AddHours(-1), 12);

        [TestMethod]
        public async Task RefreshQueuedDuringAnotherRefreshIsNotLost()
        {
            var queue = new RefreshQueue();
            var release = Signal();
            var calls = new List<string>();
            var first = queue.Enqueue(async () => { calls.Add("first-start"); await release.Task; calls.Add("first-end"); });
            var second = queue.Enqueue(() => { calls.Add("second"); return Task.CompletedTask; });
            Assert.IsTrue(queue.IsBusy);
            Assert.IsFalse(second.IsCompleted);
            release.SetResult(true);
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
            CollectionAssert.AreEqual(new[] { "first-start", "first-end", "second" }, calls);
            Assert.IsFalse(queue.IsBusy);
        }

        [TestMethod]
        public async Task FailedRefreshDoesNotBlockNextQueuedRequest()
        {
            var queue = new RefreshQueue();
            var release = Signal();
            var first = queue.Enqueue(async () => { await release.Task; throw new InvalidOperationException("failure"); });
            bool ran = false;
            var second = queue.Enqueue(() => { ran = true; return Task.CompletedTask; });
            release.SetResult(true);
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => first);
            await second.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(ran);
            Assert.IsFalse(queue.IsBusy);
        }

        private sealed class Calculator : PPCalculatorScoreSaber<MockScoreSaberApi>
        {
            internal Func<int, Task<PPPScoreCollection>> Fetch = _ => Task.FromResult(Page(1, 0));
            internal Action? Committing;
            internal Calculator(PPPMapPool pool) : base(new Dictionary<string, PPPMapPool> { [pool.Id] = pool }, Settings(), _ => new PPPBeatMapInfo()) { }
            internal override Task<PPPScoreCollection> GetRecentScores(string userId, int pageSize, int page, PPPMapPool mapPool) => Fetch(page);
            protected override void OnScoresCommitted(PPPMapPool mapPool) => Committing?.Invoke();
        }

        private static PPPScoreCollection Page(int page, int total, string? hash = null, double pp = 100) => new(new ScoreSaberPlayerScoreList
        {
            metadata = new ScoreSaberScoreListMetadata { page = page, itemsPerPage = 1, total = total },
            playerScores = hash == null ? new List<ScoreSaberPlayerScore>() : new List<ScoreSaberPlayerScore>
            {
                new()
                {
                    score = new ScoreSaberScore { pp = pp, timeSet = DateTimeOffset.UtcNow },
                    leaderboard = new ScoreSaberLeaderboardInfo
                    {
                        songHash = hash,
                        difficulty = new ScoreSaberDifficulty { gameMode = "SoloStandard", difficulty = 9 }
                    }
                }
            }
        });

        [TestMethod]
        public async Task ConfirmationRequiresFreshApiResponseNotCachedScore()
        {
            var pool = new PPPMapPool();
            pool.LsScores.Add(new ShortScore("TARGET_SOLOSTANDARD_9", 500));
            var calculator = new Calculator(pool) { Fetch = _ => Task.FromResult(Page(1, 1, "OTHER")) };
            var expected = new PPPScoreSetData { hash = "TARGET_SOLOSTANDARD_9", pp = 500 };
            Assert.IsFalse(await calculator.GetPlayerScores(pool, 10, 100, expectedScore: expected));
            Assert.IsTrue(pool.ScoreRefreshFailed);
            calculator.Fetch = _ => Task.FromResult(Page(1, 1, "TARGET", 500));
            Assert.IsTrue(await calculator.GetPlayerScores(pool, 10, 100, expectedScore: expected));
            Assert.IsFalse(pool.ScoreRefreshFailed);
        }

        [TestMethod]
        public async Task OldApiScoreDoesNotConfirmNewImprovement()
        {
            var pool = new PPPMapPool();
            var calculator = new Calculator(pool) { Fetch = _ => Task.FromResult(Page(1, 1, "TARGET", 100)) };
            var expected = new PPPScoreSetData { hash = "TARGET_SOLOSTANDARD_9", pp = 120 };
            Assert.IsFalse(await calculator.GetPlayerScores(pool, 10, 100, expectedScore: expected));
            calculator.Fetch = _ => Task.FromResult(Page(1, 1, "TARGET", 120));
            Assert.IsTrue(await calculator.GetPlayerScores(pool, 10, 100, expectedScore: expected));
            Assert.AreEqual(120d, pool.LsScores.Single().Pp);
        }

        [TestMethod]
        public async Task ConfirmationCanLookPastFirstOldCachedScore()
        {
            var pool = new PPPMapPool { DtLastScoreSet = DateTimeOffset.UtcNow.AddDays(1) };
            pool.LsScores.Add(new ShortScore("OLD_SOLOSTANDARD_9", 100));
            var calculator = new Calculator(pool)
            {
                Fetch = page => Task.FromResult(Page(page, 2, page == 1 ? "OLD" : "TARGET", 100))
            };
            Assert.IsTrue(await calculator.GetPlayerScores(pool, 1, 1,
                expectedScore: new PPPScoreSetData { hash = "TARGET_SOLOSTANDARD_9", pp = 100 }));
        }

        [TestMethod]
        public async Task MissingConfirmationHasBoundedLookback()
        {
            var pool = new PPPMapPool();
            pool.LsScores.Add(new ShortScore("OLD_SOLOSTANDARD_9", 100));
            int pages = 0;
            var calculator = new Calculator(pool) { Fetch = page => { pages++; return Task.FromResult(Page(page, 100, "OLD")); } };
            Assert.IsFalse(await calculator.GetPlayerScores(pool, 1, 1,
                expectedScore: new PPPScoreSetData { hash = "TARGET_SOLOSTANDARD_9", pp = 100 }));
            Assert.AreEqual(3, pages);
        }

        [TestMethod]
        public async Task ColdCacheIsFullyFetchedEvenWhenCallerRequestedOnePage()
        {
            var pool = new PPPMapPool();
            var calculator = new Calculator(pool) { Fetch = page => Task.FromResult(Page(page, 2, "MAP" + page)) };
            Assert.IsTrue(await calculator.GetPlayerScores(pool, 1, 1, true));
            Assert.AreEqual(2, pool.LsScores.Count);
        }

        [TestMethod]
        public async Task CalculationDoesNotWaitForHttpButCannotReadMidCommit()
        {
            var pool = new PPPMapPool { AccumulationConstant = 0.965f, CurrentPlayer = new PPPPlayer { Pp = 200 } };
            pool.LsScores.Add(new ShortScore("OLD_SOLOSTANDARD_9", 200));
            var networkEntered = Signal();
            var releaseNetwork = Signal();
            var commitEntered = Signal();
            using var releaseCommit = new ManualResetEventSlim();
            var calculator = new Calculator(pool)
            {
                Fetch = async _ => { networkEntered.SetResult(true); await releaseNetwork.Task; return Page(1, 1, "TARGET", 300); },
                Committing = () => { commitEntered.SetResult(true); releaseCommit.Wait(TimeSpan.FromSeconds(5)); }
            };
            var refresh = Task.Run(() => calculator.GetPlayerScores(pool, 10, 100));
            try
            {
                await networkEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var duringHttp = await Task.Run(() => calculator.GetPlayerScorePPGain("OLD_SOLOSTANDARD_9", 200, pool))
                    .WaitAsync(TimeSpan.FromSeconds(2));
                Assert.AreEqual(0d, duringHttp.PpGainWeighted);
                releaseNetwork.SetResult(true);
                await commitEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var calculationStarted = Signal();
                var duringCommit = Task.Run(() =>
                {
                    calculationStarted.SetResult(true);
                    return calculator.GetPlayerScorePPGain("TARGET_SOLOSTANDARD_9", 300, pool);
                });
                await calculationStarted.Task;
                Assert.IsFalse(duringCommit.IsCompleted);
                releaseCommit.Set();
                await refresh.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.AreEqual(0d, (await duringCommit).PpGainWeighted);
                Assert.AreEqual(pool.LsScores.Count, pool.DctScorePositionLookup.Count);
                Assert.AreEqual(1, pool.LsScores.Count, "The complete response must remove scores absent from the server snapshot.");
                Assert.AreEqual(300, pool.LsScores.Last().WeightedSum);
            }
            finally { releaseNetwork.TrySetResult(true); releaseCommit.Set(); await refresh; }
        }

        [TestMethod]
        public async Task SaveDataDoesNotExposeLiveScoreLists()
        {
            var pool = new PPPMapPool();
            pool.LsScores.Add(new ShortScore("OLD_SOLOSTANDARD_9", 200));
            var instance = await CalculatorInstance.CreateAsyncMock<MockScoreSaberApi, MockBeatLeader, MockHitBloq, MockAccSaber>(Settings(),
                new Dictionary<string, PPPredictor.Core.DataType.LeaderBoard.LeaderboardData>
                {
                    ["ScoreSaber"] = new() { DctMapPool = new Dictionary<string, PPPMapPool> { [pool.Id] = pool } }
                }, _ => new PPPBeatMapInfo());
            var saved = instance.GetSaveData()["ScoreSaber"].DctMapPool[pool.Id];
            pool.LsScores[0].Pp = 500;
            Assert.AreEqual(200d, saved.LsScores[0].Pp);
        }
    }
}
