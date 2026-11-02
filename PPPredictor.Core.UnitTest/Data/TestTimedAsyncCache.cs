using PPPredictor.Core;

namespace UnitTests.Data
{
    [TestClass]
    public class TestTimedAsyncCache
    {
        [TestMethod]
        public async Task ConcurrentRequestsShareFetchAndReadsDoNotExtendExpiry()
        {
            var now = DateTime.UtcNow;
            var cache = new TimedAsyncCache<string>(TimeSpan.FromMinutes(5), () => now);
            var pending = new TaskCompletionSource<string>();
            int calls = 0;
            Task<string> Fetch() { calls++; return pending.Task; }
            var first = cache.Get("HASH", Fetch);
            var second = cache.Get("hash", Fetch);
            Assert.AreSame(first, second);
            pending.SetResult("old");
            var original = await first;
            now = now.AddMinutes(4);
            Assert.AreSame(original, await cache.Get("hash", Fetch));
            now = now.AddMinutes(1);
            var fresh = await cache.Get("hash", () => { calls++; return Task.FromResult("new"); });
            Assert.AreEqual(2, calls);
            Assert.AreEqual("new", fresh.Value);
            Assert.AreEqual(now, fresh.FetchedAtUtc);
        }

        [TestMethod]
        public async Task FailureRetainsDataAndTimestampThenRetriesAfterCooldown()
        {
            var now = DateTime.UtcNow;
            var cache = new TimedAsyncCache<string>(TimeSpan.FromMinutes(5), () => now);
            var original = await cache.Get("map", () => Task.FromResult("old"));
            now = now.AddMinutes(5);
            var failed = await cache.Get("map", () => throw new IOException("offline"));
            Assert.AreEqual("old", failed.Value);
            Assert.AreEqual(original.FetchedAtUtc, failed.FetchedAtUtc);
            Assert.IsNotNull(failed.Error);
            now = now.AddSeconds(14);
            Assert.AreSame(failed, await cache.Get("map", () => throw new Exception("must not fetch")));
            now = now.AddSeconds(1);
            var recovered = await cache.Get("map", () => Task.FromResult("new"));
            Assert.IsNull(recovered.Error);
            Assert.AreEqual("new", recovered.Value);
        }

        [TestMethod]
        public async Task ConfirmedAbsenceHasShortExpiryAndIsNotAnError()
        {
            var now = DateTime.UtcNow;
            var cache = new TimedAsyncCache<string>(TimeSpan.FromMinutes(5), () => now);
            var missing = await cache.Get("map", () => Task.FromResult<string>(null!));
            Assert.IsNull(missing.Value);
            Assert.IsNull(missing.Error);
            now = now.AddSeconds(59);
            Assert.AreSame(missing, await cache.Get("map", () => throw new Exception("must not fetch")));
            now = now.AddSeconds(1);
            Assert.AreEqual("ranked", (await cache.Get("map", () => Task.FromResult("ranked"))).Value);
        }

        [TestMethod]
        public async Task ClearDoesNotAllowOldPendingRequestToRepopulateCache()
        {
            var cache = new TimedAsyncCache<string>(TimeSpan.FromMinutes(5));
            var pending = new TaskCompletionSource<string>();
            var old = cache.Get("map", () => pending.Task);
            cache.Clear();
            await cache.Get("map", () => Task.FromResult("new"));
            pending.SetResult("old");
            await old;
            Assert.AreEqual("new", (await cache.Get("map", () => throw new Exception("must not fetch"))).Value);
        }
    }
}
