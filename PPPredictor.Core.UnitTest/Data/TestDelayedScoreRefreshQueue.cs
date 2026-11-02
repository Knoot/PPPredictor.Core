using System.Collections.Concurrent;
using PPPredictor.Core;
using PPPredictor.Core.DataType;
using static PPPredictor.Core.DataType.Enums;

namespace UnitTests.Data
{
    [TestClass]
    public class TestDelayedScoreRefreshQueue
    {
        private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        [TestMethod]
        public async Task DuplicateSourcesShareDelayAndPreserveHighestSourceScore()
        {
            var release = Signal();
            int waits = 0;
            PPPScoreSetData? received = null;
            using var queue = new DelayedScoreRefreshQueue((_, data) => received = data, TimeSpan.FromSeconds(5),
                (_, _) => { waits++; return release.Task; });
            var first = queue.Schedule(Leaderboard.AccSaber, new PPPScoreSetData { hash = "MAP_SOLOSTANDARD_9", score = 1000 });
            var second = queue.Schedule(Leaderboard.AccSaber, new PPPScoreSetData { hash = "map_solostandard_9", score = 900 });
            Assert.AreSame(first, second);
            Assert.AreEqual(1, waits);
            Assert.IsNull(received);
            release.SetResult(true);
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsNotNull(received);
            Assert.AreEqual(1000L, received.score);
        }

        [TestMethod]
        public async Task LeaderboardsAndDifficultiesHaveIndependentRefreshes()
        {
            var release = Signal();
            var calls = new ConcurrentQueue<string>();
            using var queue = new DelayedScoreRefreshQueue((leaderboard, data) => calls.Enqueue($"{leaderboard}:{data.hash}"),
                TimeSpan.Zero, (_, _) => release.Task);
            var first = queue.Schedule(Leaderboard.AccSaber, new PPPScoreSetData { hash = "MAP_SOLOSTANDARD_9" });
            var second = queue.Schedule(Leaderboard.HitBloq, new PPPScoreSetData { hash = "MAP_SOLOSTANDARD_9" });
            var third = queue.Schedule(Leaderboard.AccSaber, new PPPScoreSetData { hash = "MAP_SOLOSTANDARD_7" });
            release.SetResult(true);
            await Task.WhenAll(first, second, third).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(3, calls.Count);
        }

        [TestMethod]
        public async Task SameMapCanBeRefreshedAgainAfterCompletion()
        {
            int calls = 0;
            using var queue = new DelayedScoreRefreshQueue((_, _) => calls++, TimeSpan.Zero);
            await queue.Schedule(Leaderboard.HitBloq, new PPPScoreSetData { hash = "MAP" });
            await queue.Schedule(Leaderboard.HitBloq, new PPPScoreSetData { hash = "MAP" });
            Assert.AreEqual(2, calls);
        }

        [TestMethod]
        public async Task DispatchFailureDoesNotKeepKeyBlocked()
        {
            int calls = 0;
            using var queue = new DelayedScoreRefreshQueue((_, _) =>
            {
                if (++calls == 1) throw new InvalidOperationException("failure");
            }, TimeSpan.Zero);
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => queue.Schedule(Leaderboard.AccSaber, new PPPScoreSetData { hash = "MAP" }));
            await queue.Schedule(Leaderboard.AccSaber, new PPPScoreSetData { hash = "MAP" });
            Assert.AreEqual(2, calls);
        }

        [TestMethod]
        public async Task CancelledOldDelayCannotRemoveReplacementRequest()
        {
            var oldRelease = Signal();
            var newRelease = Signal();
            int waits = 0, calls = 0;
            using var queue = new DelayedScoreRefreshQueue((_, _) => Interlocked.Increment(ref calls), TimeSpan.Zero,
                (_, _) => ++waits == 1 ? oldRelease.Task : newRelease.Task);
            var oldRequest = queue.Schedule(Leaderboard.AccSaber, new PPPScoreSetData { hash = "MAP" });
            queue.CancelPending();
            await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => oldRequest);
            var replacement = queue.Schedule(Leaderboard.AccSaber, new PPPScoreSetData { hash = "MAP" });
            oldRelease.SetResult(true);
            Assert.AreSame(replacement, queue.Schedule(Leaderboard.AccSaber, new PPPScoreSetData { hash = "MAP" }));
            newRelease.SetResult(true);
            await replacement.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(1, calls);
        }

        [TestMethod]
        public async Task DisposeCancelsWaitingRefresh()
        {
            int calls = 0;
            var queue = new DelayedScoreRefreshQueue((_, _) => calls++, TimeSpan.FromHours(1));
            var task = queue.Schedule(Leaderboard.AccSaber, new PPPScoreSetData { hash = "MAP" });
            queue.Dispose();
            await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => task);
            Assert.AreEqual(0, calls);
            Assert.ThrowsException<ObjectDisposedException>(() => queue.Schedule(Leaderboard.AccSaber, new PPPScoreSetData { hash = "MAP" }));
        }
    }
}
