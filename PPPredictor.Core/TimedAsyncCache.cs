using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PPPredictor.Core
{
    internal sealed class CachedResult<T> where T : class
    {
        internal T Value { get; }
        internal Exception Error { get; }
        internal DateTime FetchedAtUtc { get; }
        internal CachedResult(T value, Exception error, DateTime fetchedAtUtc)
        {
            Value = value; Error = error; FetchedAtUtc = fetchedAtUtc;
        }
    }

    internal sealed class TimedAsyncCache<T> where T : class
    {
        private sealed class Entry
        {
            internal CachedResult<T> Result;
            internal DateTime ExpiresUtc;
            internal Task<CachedResult<T>> Pending;
        }
        private readonly object gate = new object();
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly TimeSpan positiveTtl, negativeTtl, errorTtl;
        private readonly Func<DateTime> utcNow;

        internal TimedAsyncCache(TimeSpan positiveTtl, Func<DateTime> utcNow = null,
            TimeSpan? negativeTtl = null, TimeSpan? errorTtl = null)
        {
            this.positiveTtl = positiveTtl;
            this.negativeTtl = negativeTtl ?? TimeSpan.FromMinutes(1);
            this.errorTtl = errorTtl ?? TimeSpan.FromSeconds(15);
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        internal Task<CachedResult<T>> Get(string key, Func<Task<T>> fetch)
        {
            Entry entry;
            TaskCompletionSource<CachedResult<T>> completion;
            lock (gate)
            {
                if (!entries.TryGetValue(key, out entry))
                {
                    if (entries.Count >= 512)
                    {
                        var expired = new List<string>();
                        foreach (var pair in entries)
                            if (pair.Value.Pending == null && pair.Value.ExpiresUtc <= utcNow()) expired.Add(pair.Key);
                        foreach (var expiredKey in expired) entries.Remove(expiredKey);
                        if (entries.Count >= 512)
                        {
                            foreach (var pair in entries)
                                if (pair.Value.Pending == null) { expired.Add(pair.Key); break; }
                            if (expired.Count > 0) entries.Remove(expired[expired.Count - 1]);
                        }
                    }
                    entries[key] = entry = new Entry();
                }
                if (entry.Pending != null) return entry.Pending;
                if (entry.Result != null && entry.ExpiresUtc > utcNow()) return Task.FromResult(entry.Result);
                completion = new TaskCompletionSource<CachedResult<T>>(TaskCreationOptions.RunContinuationsAsynchronously);
                entry.Pending = completion.Task;
            }
            _ = Fetch(entry, fetch, completion);
            return completion.Task;
        }

        private async Task Fetch(Entry entry, Func<Task<T>> fetch, TaskCompletionSource<CachedResult<T>> completion)
        {
            CachedResult<T> result;
            TimeSpan ttl;
            try
            {
                T value = await fetch();
                result = new CachedResult<T>(value, null, utcNow());
                ttl = value == null ? negativeTtl : positiveTtl;
            }
            catch (Exception ex)
            {
                result = new CachedResult<T>(entry.Result?.Value, ex, entry.Result?.FetchedAtUtc ?? DateTime.MinValue);
                ttl = errorTtl;
            }
            lock (gate)
            {
                entry.Result = result;
                entry.ExpiresUtc = utcNow() + ttl;
                entry.Pending = null;
            }
            completion.TrySetResult(result);
        }

        internal void Clear() { lock (gate) entries.Clear(); }
    }
}
