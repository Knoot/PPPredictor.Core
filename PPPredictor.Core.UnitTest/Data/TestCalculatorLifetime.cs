using System.Net.Http;
using System.Runtime.CompilerServices;
using PPPredictor.Core;
using PPPredictor.Core.DataType;
using PPPredictor.Core.DataType.LeaderBoard;
using PPPredictor.Core.DataType.MapPool;
using PPPredictor.Core.Interface;
using PPPredictor.Core.UnitTest.MockService;
using static PPPredictor.Core.DataType.Enums;
using static PPPredictor.Core.DataType.LeaderBoard.ScoreSaberDataTypes;

namespace UnitTests.Data
{
    [TestClass]
    [DoNotParallelize]
    public class TestCalculatorLifetime
    {
        private static Settings Settings() => new(true, false, false, false, "123", PPGainCalculationType.Weighted,
            MapPoolSorting.Alphabetical, "", -7, DateTime.Now.AddHours(-1), 12);

        [TestMethod]
        public async Task DisposedCalculatorStopsForwardingStaticLogs()
        {
            var old = new CalculatorInstance(Settings());
            var current = new CalculatorInstance(Settings());
            int oldCalls = 0, newCalls = 0;
            old.OnMessage += (_, _) => oldCalls++;
            current.OnMessage += (_, _) => newCalls++;
            await old.ShutdownAsync();
            old.Dispose();
            Logging.ErrorPrint("lifetime-test");
            Assert.AreEqual(0, oldCalls);
            Assert.AreEqual(1, newCalls);
            await current.ShutdownAsync();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference DisposedReference()
        {
            var calculator = new CalculatorInstance(Settings());
            calculator.ShutdownAsync().GetAwaiter().GetResult();
            return new WeakReference(calculator);
        }

        [TestMethod]
        public void StaticLoggingDoesNotKeepDisposedCalculatorAlive()
        {
            var reference = DisposedReference();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.IsFalse(reference.IsAlive);
        }

        private sealed class Handler : HttpMessageHandler
        {
            internal readonly TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal int Calls;
            internal bool CompleteSecond;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                int call = Interlocked.Increment(ref Calls);
                Started.TrySetResult(true);
                if (CompleteSecond && call == 2)
                    return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"metadata\":{\"page\":1,\"itemsPerPage\":1,\"total\":0},\"playerScores\":[]}")
                    };
                await Task.Delay(Timeout.Infinite, token);
                throw new AssertFailedException("canceled HTTP request should not finish normally");
            }
        }

        internal sealed class Api : MockScoreSaberApi, IScoresaberAPI, IDisposable
        {
            internal static HttpClient Client = null!;
            internal static int Disposals;
            public void Dispose() { Interlocked.Increment(ref Disposals); }
            Task<ScoreSaberPlayerScoreList> IScoresaberAPI.GetPlayerScores(string playerId, int? limit, int? page) =>
                NetworkUtil.GetDataAsync<ScoreSaberPlayerScoreList>(Client, Leaderboard.ScoreSaber, "test", "scores");
        }

        [TestMethod]
        public async Task ShutdownCancelsHttpAndQueuedOperationsWithoutCommittingOrRetrying()
        {
            var handler = new Handler();
            using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
            Api.Client = client;
            Api.Disposals = 0;
            var pool = new PPPMapPool();
            var data = new Dictionary<string, LeaderboardData> { ["ScoreSaber"] = new() { DctMapPool = new() { [pool.Id] = pool } } };
            var calculator = await CalculatorInstance.CreateAsyncMock<Api, MockBeatLeader, MockHitBloq, MockAccSaber>(Settings(), data, _ => new PPPBeatMapInfo());
            var request = calculator.GetPlayerScores(Leaderboard.ScoreSaber, pool.Id, 1, 1);
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var queued = calculator.GetPlayerScores(Leaderboard.ScoreSaber, pool.Id, 1, 1);
            var shutdown = calculator.ShutdownAsync();
            Assert.AreSame(shutdown, calculator.ShutdownAsync());
            await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(async () => await request);
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(async () => await queued);
            Assert.AreEqual(1, handler.Calls);
            Assert.IsFalse(pool.HasScoreDataFetched);
            Assert.IsFalse(pool.ScoreRefreshFailed);
            Assert.AreEqual(0, pool.LsScores.Count);
            await calculator.ShutdownAsync();
            Assert.AreEqual(1, Api.Disposals);
        }

        [TestMethod]
        public async Task DisposedRefreshQueueCancelsWaitingWorkAndRejectsNewWork()
        {
            var queue = new RefreshQueue();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var active = queue.Enqueue(async () =>
            {
                entered.SetResult(true);
                await Task.Delay(Timeout.Infinite, queue.Token);
            });
            await entered.Task;
            bool secondRan = false;
            var queued = queue.Enqueue(() => { secondRan = true; return Task.CompletedTask; });
            queue.Dispose();
            queue.Dispose();
            await Assert.ThrowsExceptionAsync<TaskCanceledException>(async () => await active);
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(async () => await queued);
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => queue.Enqueue(() => Task.CompletedTask));
            Assert.IsFalse(secondRan);
            Assert.IsFalse(queue.IsBusy);
        }

        [TestMethod]
        public async Task CancelingOneCalculatorDoesNotCancelAnotherOrLeakIntoCallerContext()
        {
            var handler = new Handler { CompleteSecond = true };
            using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
            Api.Client = client;
            var first = await CalculatorInstance.CreateAsyncMock<Api, MockBeatLeader, MockHitBloq, MockAccSaber>(
                Settings(), new(), _ => new PPPBeatMapInfo());
            var second = await CalculatorInstance.CreateAsyncMock<Api, MockBeatLeader, MockHitBloq, MockAccSaber>(
                Settings(), new(), _ => new PPPBeatMapInfo());
            var firstRequest = first.GetPlayerScores(Leaderboard.ScoreSaber, "-1", 1, 1);
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var secondRequest = second.GetPlayerScores(Leaderboard.ScoreSaber, "-1", 1, 1);
            await first.ShutdownAsync();
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(async () => await firstRequest);
            Assert.IsTrue(await secondRequest.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.IsFalse(NetworkUtil.RequestCancellation.IsCancellationRequested);
            await second.ShutdownAsync();
        }

        [TestMethod]
        public async Task FailedFactoryDoesNotLeaveStaticLogSubscription()
        {
            var field = typeof(Logging).GetField("OnMessage", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            int Count() => ((Delegate?)field.GetValue(null))?.GetInvocationList().Length ?? 0;
            int before = Count();
            await Assert.ThrowsExceptionAsync<Exception>(() => CalculatorInstance.CreateAsyncMock<MockScoreSaberApi, MockBeatLeader, MockHitBloq, MockAccSaber>(
                Settings(), new(), null!));
            Assert.AreEqual(before, Count());
        }
    }
}
