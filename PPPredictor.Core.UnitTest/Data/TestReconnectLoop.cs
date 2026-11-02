using PPPredictor.Core;

namespace UnitTests.Data
{
    [TestClass]
    public class TestReconnectLoop
    {
        private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        [TestMethod]
        public async Task ErrorAndCloseHaveOneReplacementAndReleasePreviousConnectionFirst()
        {
            var firstConnected = Signal();
            var secondConnected = Signal();
            var disconnected = Signal();
            int attempts = 0, active = 0, maximumActive = 0, released = 0, errors = 0;
            Action? lateOpen = null;
            var notifications = new List<bool>();
            using var loop = new ReconnectLoop(async (token, opened) =>
            {
                int attempt = ++attempts;
                maximumActive = Math.Max(maximumActive, ++active);
                try
                {
                    opened();
                    opened();
                    if (attempt == 1)
                    {
                        lateOpen = opened;
                        firstConnected.SetResult(true);
                        await disconnected.Task;
                    }
                    else
                    {
                        secondConnected.SetResult(true);
                        await Task.Delay(Timeout.Infinite, token);
                    }
                }
                finally { active--; released++; }
            }, _ => errors++, (_, _) => Task.CompletedTask);
            loop.Connected += isReconnect => notifications.Add(isReconnect);
            loop.Start();
            loop.Start();
            await firstConnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
            disconnected.TrySetException(new IOException("closed with error"));
            Assert.IsFalse(disconnected.TrySetResult(true));
            await secondConnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
            lateOpen!();
            Assert.AreEqual(2, attempts);
            Assert.AreEqual(1, maximumActive);
            Assert.AreEqual(1, released);
            Assert.AreEqual(1, errors);
            CollectionAssert.AreEqual(new[] { false, true }, notifications);
            loop.Dispose();
            await loop.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(0, active);
            Assert.AreEqual(2, released);
        }

        [TestMethod]
        public async Task FailedHandshakesUseBoundedBackoffAndRecoveryIsReported()
        {
            var connected = Signal();
            var pauses = new List<double>();
            int attempts = 0;
            bool recovered = false;
            using var loop = new ReconnectLoop(async (token, opened) =>
            {
                if (++attempts <= 7) throw new IOException("offline");
                opened();
                connected.SetResult(true);
                await Task.Delay(Timeout.Infinite, token);
            }, _ => { }, (duration, _) => { pauses.Add(duration.TotalSeconds); return Task.CompletedTask; });
            loop.Connected += isReconnect => recovered = isReconnect;
            loop.Start();
            await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
            CollectionAssert.AreEqual(new[] { 1d, 2d, 4d, 8d, 16d, 30d, 30d }, pauses);
            Assert.IsTrue(recovered);
            loop.Dispose();
            await loop.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }

        [TestMethod]
        public async Task StopDuringBackoffCancelsRetryAndCannotBeRestarted()
        {
            var waiting = Signal();
            int attempts = 0;
            using var loop = new ReconnectLoop((_, _) => { attempts++; throw new IOException("offline"); },
                _ => { }, async (_, token) =>
                {
                    waiting.SetResult(true);
                    await Task.Delay(Timeout.Infinite, token);
                });
            loop.Start();
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
            loop.Dispose();
            loop.Dispose();
            await loop.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            loop.Start();
            Assert.AreEqual(1, attempts);
        }

        [TestMethod]
        public async Task StopBeforeStartNeverCreatesConnection()
        {
            using var loop = new ReconnectLoop((_, _) => throw new AssertFailedException("must not connect"), _ => { });
            loop.Dispose();
            loop.Start();
            await loop.Completion;
        }

        [TestMethod]
        public async Task NormalCloseAlsoReconnectsWithoutErrorEvent()
        {
            var connected = Signal();
            int attempts = 0, recoveries = 0, errors = 0;
            using var loop = new ReconnectLoop(async (token, opened) =>
            {
                opened();
                if (++attempts == 1) return;
                connected.SetResult(true);
                await Task.Delay(Timeout.Infinite, token);
            }, _ => errors++, (_, _) => Task.CompletedTask);
            loop.Connected += recovered => { if (recovered) recoveries++; };
            loop.Start();
            await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(1, recoveries);
            Assert.AreEqual(0, errors);
            loop.Dispose();
            await loop.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }

        [TestMethod]
        public async Task BriefConnectionsDoNotResetBackoffButStableConnectionDoes()
        {
            var now = DateTime.UtcNow;
            var connected = Signal();
            var pauses = new List<double>();
            int attempts = 0;
            using var loop = new ReconnectLoop(async (token, opened) =>
            {
                opened();
                if (++attempts == 3) now = now.AddSeconds(30);
                if (attempts < 5) return;
                connected.SetResult(true);
                await Task.Delay(Timeout.Infinite, token);
            }, _ => { }, (duration, _) => { pauses.Add(duration.TotalSeconds); return Task.CompletedTask; }, () => now);
            loop.Start();
            await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
            CollectionAssert.AreEqual(new[] { 1d, 2d, 1d, 2d }, pauses);
            loop.Dispose();
            await loop.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
