using System;
using System.Threading;
using System.Threading.Tasks;

namespace PPPredictor.Core
{
    internal sealed class RefreshQueue : IDisposable
    {
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private int pending;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly object state = new object();
        private bool stopped, canceling, resourcesReleased;
        internal CancellationToken Token { get; }
        internal RefreshQueue() { Token = lifetime.Token; }

        internal bool IsBusy => Volatile.Read(ref pending) > 0;

        internal async Task Enqueue(Func<Task> refresh)
        {
            lock (state) { Token.ThrowIfCancellationRequested(); pending++; }
            try
            {
                await gate.WaitAsync(Token);
                try { Token.ThrowIfCancellationRequested(); await refresh(); }
                finally { gate.Release(); }
            }
            finally
            {
                lock (state)
                {
                    pending--;
                    if (stopped && !canceling && pending == 0) ReleaseResources();
                }
            }
        }
        public void Dispose()
        {
            lock (state)
            {
                if (stopped) return;
                stopped = true;
                canceling = true;
                lifetime.Cancel();
                canceling = false;
                if (pending == 0) ReleaseResources();
            }
        }
        private void ReleaseResources()
        {
            if (resourcesReleased) return;
            resourcesReleased = true;
            lifetime.Dispose();
            gate.Dispose();
        }
    }
}
