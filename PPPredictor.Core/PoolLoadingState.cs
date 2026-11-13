using System;
using System.Collections.Generic;

namespace PPPredictor.Core
{
    internal sealed class PoolLoadingState
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);

        internal void SetLoading(string poolId, bool loading)
        {
            if (poolId == null) throw new ArgumentNullException(nameof(poolId));
            lock (gate)
            {
                counts.TryGetValue(poolId, out int count);
                count = Math.Max(0, count + (loading ? 1 : -1));
                if (count == 0) counts.Remove(poolId);
                else counts[poolId] = count;
            }
        }

        internal bool IsLoading(string poolId)
        {
            if (poolId == null) return false;
            lock (gate) return counts.ContainsKey(poolId);
        }
    }
}
