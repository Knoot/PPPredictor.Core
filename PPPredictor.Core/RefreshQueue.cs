using System;
using System.Threading;
using System.Threading.Tasks;

namespace PPPredictor.Core
{
    internal sealed class RefreshQueue
    {
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private int pending;

        internal bool IsBusy => Volatile.Read(ref pending) > 0;

        internal async Task Enqueue(Func<Task> refresh)
        {
            Interlocked.Increment(ref pending);
            try
            {
                await gate.WaitAsync();
                try { await refresh(); }
                finally { gate.Release(); }
            }
            finally { Interlocked.Decrement(ref pending); }
        }
    }
}
