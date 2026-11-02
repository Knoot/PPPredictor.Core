using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using PPPredictor.Core.DataType;

namespace PPPredictor.Core
{
    public enum FullScoreSyncReason { None, ScoreSubmitted, Overdue, TotalMismatch, MissingCache, RatingChanged, Manual }

    public sealed class ScoreSyncState
    {
        public DateTime LastFullSyncUtc { get; set; }
        public DateTime RetryAfterUtc { get; set; }
        public bool HasCompleteScores { get; set; }
        public string OwnerId { get; set; }
        public FullScoreSyncReason PendingReason { get; set; }

        internal long ScoreRevision, ProfileRevision, ObservedScoreRevision, ObservedProfileRevision;
        internal bool LastFetchWasComplete;
        internal bool LastFetchUnconfirmed;
        internal DateTime? FirstMismatchUtc;
        internal int MismatchDirection;
        internal DateTime SuppressMismatchUntilUtc;
        [JsonProperty]
        internal readonly Dictionary<string, PPPScoreSetData> ExpectedScores = new Dictionary<string, PPPScoreSetData>(StringComparer.OrdinalIgnoreCase);

        internal void AssignOwner(string ownerId)
        {
            if (OwnerId == ownerId) return;
            if (!string.IsNullOrEmpty(OwnerId))
            {
                ExpectedScores.Clear();
                RetryAfterUtc = DateTime.MinValue;
                Request(FullScoreSyncReason.MissingCache);
            }
            OwnerId = ownerId;
            HasCompleteScores = false;
            LastFullSyncUtc = DateTime.MinValue;
            ResetMismatch();
        }

        internal void Request(FullScoreSyncReason reason)
        {
            if (reason > PendingReason) PendingReason = reason;
        }

        internal void CheckStartup(DateTime now, bool loaded)
        {
            if (loaded && (LastFullSyncUtc == DateTime.MinValue || now - LastFullSyncUtc >= TimeSpan.FromDays(7)))
                Request(FullScoreSyncReason.Overdue);
        }

        internal bool CanRun(DateTime now) => PendingReason != FullScoreSyncReason.None && now >= RetryAfterUtc;
        internal bool NeedsConfirmation(DateTime now) => FirstMismatchUtc.HasValue
            && now - FirstMismatchUtc.Value >= TimeSpan.FromSeconds(60) && ExpectedScores.Count == 0
            && now >= SuppressMismatchUntilUtc;

        internal void ResetMismatch(bool cancelRequest = false)
        {
            FirstMismatchUtc = null;
            MismatchDirection = 0;
            if (cancelRequest && PendingReason == FullScoreSyncReason.TotalMismatch && RetryAfterUtc == DateTime.MinValue)
                PendingReason = FullScoreSyncReason.None;
        }

        internal void Observe(DateTime now, bool successful, double? localTotal, double serverTotal)
        {
            if (!successful || ExpectedScores.Count != 0 || now < SuppressMismatchUntilUtc)
            {
                ResetMismatch(true);
                return;
            }
            if (ScoreRevision <= ObservedScoreRevision || ProfileRevision <= ObservedProfileRevision) return;
            ObservedScoreRevision = ScoreRevision;
            ObservedProfileRevision = ProfileRevision;
            if (LastFetchWasComplete)
            {
                Complete(now);
                return;
            }
            if (!localTotal.HasValue || !Finite(localTotal.Value) || !Finite(serverTotal)
                || Math.Abs(serverTotal - localTotal.Value) <= 1 || (LastFullSyncUtc != DateTime.MinValue && now - LastFullSyncUtc < TimeSpan.FromDays(1)))
            {
                ResetMismatch(true);
                return;
            }
            int direction = Math.Sign(serverTotal - localTotal.Value);
            if (!FirstMismatchUtc.HasValue || direction != MismatchDirection)
            {
                FirstMismatchUtc = now;
                MismatchDirection = direction;
            }
            else if (now - FirstMismatchUtc.Value >= TimeSpan.FromSeconds(60)) Request(FullScoreSyncReason.TotalMismatch);
        }

        internal void Complete(DateTime now)
        {
            LastFullSyncUtc = now;
            RetryAfterUtc = DateTime.MinValue;
            PendingReason = FullScoreSyncReason.None;
            ResetMismatch();
        }

        internal void Fail(DateTime now) { RetryAfterUtc = now.AddMinutes(15); ResetMismatch(); }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
