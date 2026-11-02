using Newtonsoft.Json;
using PPPredictor.Core;
using PPPredictor.Core.DataType;

namespace UnitTests.Data
{
    [TestClass]
    public class TestScoreSyncPolicy
    {
        private static readonly DateTime Now = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
        private static void Observe(ScoreSyncState state, DateTime now, double local = 100, double server = 102, bool successful = true)
        {
            state.ScoreRevision++;
            state.ProfileRevision++;
            state.Observe(now, successful, local, server);
        }

        [TestMethod]
        public void StartupOnlySchedulesLoadedPoolsOlderThanSevenDays()
        {
            var state = new ScoreSyncState();
            state.CheckStartup(Now, false);
            Assert.AreEqual(FullScoreSyncReason.None, state.PendingReason);
            state.LastFullSyncUtc = Now.AddDays(-7).AddSeconds(1);
            state.CheckStartup(Now, true);
            Assert.AreEqual(FullScoreSyncReason.None, state.PendingReason);
            state.CheckStartup(Now.AddSeconds(1), true);
            Assert.AreEqual(FullScoreSyncReason.Overdue, state.PendingReason);
        }

        [TestMethod]
        public void MismatchNeedsTwoDistinctSuccessfulPairsAtLeastSixtySecondsApart()
        {
            var state = new ScoreSyncState();
            Observe(state, Now);
            state.Observe(Now.AddMinutes(2), true, 100, 102);
            Assert.AreEqual(FullScoreSyncReason.None, state.PendingReason, "Reading the same observation is not confirmation.");
            state.ProfileRevision++;
            state.Observe(Now.AddMinutes(2), true, 100, 102);
            Assert.AreEqual(FullScoreSyncReason.None, state.PendingReason, "A profile-only update is not confirmation.");
            Observe(state, Now.AddSeconds(59));
            Assert.AreEqual(FullScoreSyncReason.None, state.PendingReason);
            Observe(state, Now.AddSeconds(60));
            Assert.AreEqual(FullScoreSyncReason.TotalMismatch, state.PendingReason);
        }

        [DataTestMethod]
        [DataRow(101d)]
        [DataRow(100d)]
        [DataRow(99d)]
        public void DifferenceOfOneOrLessDoesNotTriggerSync(double server)
        {
            var state = new ScoreSyncState();
            Observe(state, Now, server: server);
            Observe(state, Now.AddMinutes(2), server: server);
            Assert.AreEqual(FullScoreSyncReason.None, state.PendingReason);
        }

        [TestMethod]
        public void FailuresPendingScoresAndGameplayGraceResetEvidence()
        {
            var state = new ScoreSyncState();
            Observe(state, Now);
            Observe(state, Now.AddMinutes(1), successful: false);
            Observe(state, Now.AddMinutes(2));
            Assert.AreEqual(FullScoreSyncReason.None, state.PendingReason);
            state.ExpectedScores["map"] = new PPPScoreSetData();
            Observe(state, Now.AddMinutes(3));
            Assert.IsNull(state.FirstMismatchUtc);
            state.ExpectedScores.Clear();
            state.SuppressMismatchUntilUtc = Now.AddMinutes(5);
            Observe(state, Now.AddMinutes(4));
            Assert.IsNull(state.FirstMismatchUtc);
            Observe(state, Now.AddMinutes(5));
            Observe(state, Now.AddMinutes(6));
            Assert.AreEqual(FullScoreSyncReason.TotalMismatch, state.PendingReason);
        }

        [TestMethod]
        public void SuccessfulFullSyncPreventsMismatchLoopsForTwentyFourHours()
        {
            var state = new ScoreSyncState();
            state.Complete(Now);
            Observe(state, Now.AddMinutes(1));
            Observe(state, Now.AddHours(23));
            Assert.AreEqual(FullScoreSyncReason.None, state.PendingReason);
            Observe(state, Now.AddDays(1));
            Observe(state, Now.AddDays(1).AddMinutes(1));
            Assert.AreEqual(FullScoreSyncReason.TotalMismatch, state.PendingReason);
        }

        [TestMethod]
        public void FailedSyncPersistsItsRequestAndFifteenMinuteCooldownAcrossRestarts()
        {
            var state = new ScoreSyncState { LastFullSyncUtc = Now.AddDays(-8), HasCompleteScores = true, OwnerId = "123" };
            state.Request(FullScoreSyncReason.TotalMismatch);
            state.Fail(Now);
            state = JsonConvert.DeserializeObject<ScoreSyncState>(JsonConvert.SerializeObject(state))!;
            Assert.IsFalse(state.CanRun(Now.AddMinutes(15).AddTicks(-1)));
            Assert.IsTrue(state.CanRun(Now.AddMinutes(15)));
            Assert.AreEqual(Now.AddDays(-8), state.LastFullSyncUtc);
            Assert.AreEqual("123", state.OwnerId);
            state.Request(FullScoreSyncReason.Manual);
            Assert.AreEqual(FullScoreSyncReason.Manual, state.PendingReason);
            Assert.IsFalse(state.CanRun(Now.AddMinutes(1)), "Manual sync bypasses age, not the failure cooldown.");
        }

        [TestMethod]
        public void ResolvedOrUnreliableMismatchCancelsOnlyUnstartedMismatchRequests()
        {
            var state = new ScoreSyncState();
            Observe(state, Now);
            Observe(state, Now.AddMinutes(1));
            Observe(state, Now.AddMinutes(2), server: 100);
            Assert.AreEqual(FullScoreSyncReason.None, state.PendingReason);
            Observe(state, Now.AddMinutes(3));
            Observe(state, Now.AddMinutes(4), server: 98);
            Assert.AreEqual(FullScoreSyncReason.None, state.PendingReason);
            state.Request(FullScoreSyncReason.Manual);
            Observe(state, Now.AddMinutes(5), server: 100);
            Assert.AreEqual(FullScoreSyncReason.Manual, state.PendingReason);
        }

        [TestMethod]
        public void PendingScoreSurvivesSerializationWithoutSharingMutableObjects()
        {
            var original = new ScoreSyncState { OwnerId = "123", PendingReason = FullScoreSyncReason.ScoreSubmitted };
            original.ExpectedScores["MAP"] = new PPPScoreSetData { hash = "MAP", pp = 250, score = 1000 };
            var restored = JsonConvert.DeserializeObject<ScoreSyncState>(JsonConvert.SerializeObject(original))!;
            Assert.IsTrue(restored.ExpectedScores.ContainsKey("map"));
            Assert.AreEqual(250d, restored.ExpectedScores["map"].pp);
            Assert.AreEqual(1000L, restored.ExpectedScores["map"].score);
            original.ExpectedScores["MAP"].pp = 300;
            Assert.AreEqual(250d, restored.ExpectedScores["map"].pp);
            Observe(restored, Now);
            Observe(restored, Now.AddMinutes(1));
            Assert.IsNull(restored.FirstMismatchUtc, "An unconfirmed saved result must suppress mismatch evidence.");
        }

        [TestMethod]
        public void OwnerChangeDropsOtherUsersExpectationsAndInvalidatesCompleteness()
        {
            var state = new ScoreSyncState { OwnerId = "old", HasCompleteScores = true, LastFullSyncUtc = Now, RetryAfterUtc = Now.AddMinutes(15) };
            state.ExpectedScores["MAP"] = new PPPScoreSetData { hash = "MAP", pp = 250 };
            state.AssignOwner("new");
            Assert.AreEqual("new", state.OwnerId);
            Assert.AreEqual(0, state.ExpectedScores.Count);
            Assert.IsFalse(state.HasCompleteScores);
            Assert.AreEqual(DateTime.MinValue, state.LastFullSyncUtc);
            Assert.AreEqual(DateTime.MinValue, state.RetryAfterUtc);
            Assert.AreEqual(FullScoreSyncReason.MissingCache, state.PendingReason);
        }

        [TestMethod]
        public void AssigningFirstOwnerDoesNotBypassAnExistingFailureCooldown()
        {
            var state = new ScoreSyncState { RetryAfterUtc = Now.AddMinutes(15), PendingReason = FullScoreSyncReason.Manual };
            state.AssignOwner("123");
            Assert.IsFalse(state.CanRun(Now));
            Assert.IsTrue(state.CanRun(Now.AddMinutes(15)));
        }
    }
}
