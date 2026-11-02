using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PPPredictor.Core.DataType;
using static PPPredictor.Core.DataType.Enums;

namespace PPPredictor.Core
{
    internal sealed class DelayedScoreRefreshQueue : IDisposable
    {
        private sealed class Pending
        {
            internal PPPScoreSetData Data;
            internal readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            internal readonly TaskCompletionSource<bool> Completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private readonly object gate = new object();
        private readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>();
        private readonly Action<Leaderboard, PPPScoreSetData> refresh;
        private readonly Func<TimeSpan, CancellationToken, Task> wait;
        private readonly TimeSpan delay;
        private bool disposed;

        internal DelayedScoreRefreshQueue(Action<Leaderboard, PPPScoreSetData> refresh, TimeSpan delay,
            Func<TimeSpan, CancellationToken, Task> wait = null)
        {
            this.refresh = refresh;
            this.delay = delay;
            this.wait = wait ?? Task.Delay;
        }

        internal Task Schedule(Leaderboard leaderboard, PPPScoreSetData data)
        {
            string key = $"{leaderboard}_{data.hash?.ToUpperInvariant()}";
            var copy = new PPPScoreSetData
            {
                hash = data.hash, leaderboardName = data.leaderboardName, userId = data.userId,
                context = data.context, pp = data.pp, score = data.score
            };
            Pending item;
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(DelayedScoreRefreshQueue));
                if (pending.TryGetValue(key, out item))
                {
                    if (item.Data.score.HasValue && (!copy.score.HasValue || item.Data.score.Value > copy.score.Value))
                        copy.score = item.Data.score;
                    item.Data = copy;
                    return item.Completion.Task;
                }
                item = new Pending { Data = copy };
                pending.Add(key, item);
            }
            _ = Run(leaderboard, key, item);
            return item.Completion.Task;
        }

        private async Task Run(Leaderboard leaderboard, string key, Pending item)
        {
            try
            {
                await wait(delay, item.Cancellation.Token);
                PPPScoreSetData data;
                lock (gate)
                {
                    if (disposed) { item.Completion.TrySetCanceled(); return; }
                    if (!pending.TryGetValue(key, out var current) || !ReferenceEquals(current, item)) return;
                    pending.Remove(key);
                    data = item.Data;
                }
                item.Cancellation.Token.ThrowIfCancellationRequested();
                refresh(leaderboard, data);
                item.Completion.TrySetResult(true);
            }
            catch (OperationCanceledException) { item.Completion.TrySetCanceled(); }
            catch (Exception ex) { item.Completion.TrySetException(ex); }
            finally
            {
                lock (gate)
                {
                    if (pending.TryGetValue(key, out var current) && ReferenceEquals(current, item)) pending.Remove(key);
                }
                item.Cancellation.Dispose();
            }
        }

        internal void CancelPending()
        {
            Pending[] items;
            lock (gate) { items = pending.Values.ToArray(); pending.Clear(); }
            foreach (var item in items)
            {
                item.Completion.TrySetCanceled();
                try { item.Cancellation.Cancel(); }
                catch (ObjectDisposedException) { }
            }
        }

        public void Dispose()
        {
            lock (gate) disposed = true;
            CancelPending();
        }
    }
}
