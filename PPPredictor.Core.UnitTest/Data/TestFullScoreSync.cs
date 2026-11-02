using PPPredictor.Core;
using PPPredictor.Core.DataType;
using PPPredictor.Core.DataType.Curve;
using PPPredictor.Core.DataType.LeaderBoard;
using PPPredictor.Core.DataType.MapPool;
using PPPredictor.Core.DataType.Score;
using PPPredictor.Core.Interface;
using PPPredictor.Core.UnitTest.MockService;
using static PPPredictor.Core.DataType.Enums;
using static PPPredictor.Core.DataType.LeaderBoard.ScoreSaberDataTypes;
using static PPPredictor.Core.DataType.LeaderBoard.BeatLeaderDataTypes;

namespace UnitTests.Data
{
    [TestClass]
    [DoNotParallelize]
    public class TestFullScoreSync
    {
        internal sealed class Api : MockScoreSaberApi, IScoresaberAPI
        {
            internal static Func<int, Task<ScoreSaberPlayerScoreList>> Fetch = page => Task.FromResult(Page(page));
            internal static bool FailProfile;
            internal static int Calls;
            Task<ScoreSaberPlayerScoreList> IScoresaberAPI.GetPlayerScores(string id, int? limit, int? page)
            { Interlocked.Increment(ref Calls); return Fetch(page ?? 1); }
            Task<ScoreSaberPlayer> IScoresaberAPI.GetPlayer(long id) => FailProfile
                ? throw new IOException("profile offline") : Task.FromResult(new ScoreSaberPlayer { pp = 500, rank = 10 });
        }

        internal sealed class BeatApi : MockBeatLeader, IBeatLeaderAPI
        {
            internal static readonly TaskCompletionSource<bool> Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<BeatLeaderPlayer> IBeatLeaderAPI.GetPlayer(long id, long context) => Task.FromResult(new BeatLeaderPlayer { pp = 0 });
            Task<BeatLeaderPlayerScoreList> IBeatLeaderAPI.GetPlayerScores(string id, string sort, string order, int page, int count, long context, long? eventId)
            {
                Entered.TrySetResult(true);
                return Task.FromResult(new BeatLeaderPlayerScoreList
                    { metadata = new BeatLeaderPlayerScoreListMetaData { page = page, itemsPerPage = count, total = 0 } });
            }
        }

        [TestInitialize]
        public void Initialize() { Api.Calls = 0; Api.FailProfile = false; Api.Fetch = page => Task.FromResult(Page(page)); }

        private static ScoreSaberPlayerScoreList Page(int page) => new()
        {
            metadata = new ScoreSaberScoreListMetadata { page = page, itemsPerPage = 1, total = 2 },
            playerScores = new()
            {
                new ScoreSaberPlayerScore
                {
                    score = new ScoreSaberScore { pp = page == 1 ? 100 : 150, timeSet = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    leaderboard = new ScoreSaberLeaderboardInfo { songHash = page == 1 ? "OLD" : "NEW",
                        difficulty = new ScoreSaberDifficulty { gameMode = "SoloStandard", difficulty = 9 } }
                }
            }
        };

        private static PPPMapPool Pool()
        {
            var pool = new PPPMapPool { AccumulationConstant = 0.965f,
                CurrentPlayer = new PPPPlayer { Pp = 999 }, SessionPlayer = new PPPPlayer { Pp = 800 },
                DtLastScoreSet = DateTimeOffset.UtcNow.AddDays(1) };
            pool.CurveInfo = new CurveInfo(CurveType.ScoreSaber);
            pool.LsScores.AddRange(new[] { new ShortScore("OLD_SOLOSTANDARD_9", 200), new ShortScore("DELETED_SOLOSTANDARD_9", 300) });
            pool.ScoreSync.HasCompleteScores = true;
            pool.ScoreSync.OwnerId = "123";
            pool.ScoreSync.LastFullSyncUtc = DateTime.UtcNow.AddDays(-8);
            return pool;
        }

        private static Task<CalculatorInstance> Create(PPPMapPool pool, bool beatLeader = false) =>
            CalculatorInstance.CreateAsyncMock<Api, BeatApi, MockHitBloq, MockAccSaber>(
                new Settings(true, beatLeader, false, false, "123", PPGainCalculationType.Weighted,
                    MapPoolSorting.Alphabetical, "", -7, DateTime.Now.AddHours(-1), 12),
                new() { ["ScoreSaber"] = new LeaderboardData { DctMapPool = new() { [pool.Id] = pool } } }, _ => new PPPBeatMapInfo());

        [TestMethod]
        public async Task FullSyncReplacesSnapshotAndDuplicateRequestsDoNotReloadIt()
        {
            var pool = Pool();
            var calculator = await Create(pool);
            try
            {
                calculator.RequestFullScoreSync(Leaderboard.ScoreSaber, "-1", FullScoreSyncReason.Manual);
                var first = calculator.SynchronizeScores(Leaderboard.ScoreSaber, "-1", default);
                var duplicate = calculator.SynchronizeScores(Leaderboard.ScoreSaber, "-1", default);
                Assert.IsTrue(await first);
                Assert.IsFalse(await duplicate);
                Assert.AreEqual(2, Api.Calls);
                Assert.AreEqual(2, pool.LsScores.Count);
                Assert.AreEqual(100, pool.LsScores.Single(x => x.Searchstring == "OLD_SOLOSTANDARD_9").Pp);
                Assert.IsFalse(pool.LsScores.Any(x => x.Searchstring == "DELETED_SOLOSTANDARD_9"));
                Assert.AreEqual(500, pool.CurrentPlayer.Pp);
                Assert.AreEqual(800, pool.SessionPlayer.Pp);
                Assert.AreEqual(2020, pool.DtLastScoreSet.Year);
                Assert.IsTrue(pool.ScoreSync.LastFullSyncUtc > DateTime.UtcNow.AddMinutes(-1));
                Assert.AreEqual(FullScoreSyncReason.None, pool.ScoreSync.PendingReason);
            }
            finally { await calculator.ShutdownAsync(); }
        }

        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public async Task ProfileOrNextPageFailurePreservesOldCacheAndSetsRetry(bool profileFailure)
        {
            var pool = Pool();
            var calculator = await Create(pool);
            try
            {
                var original = pool.LsScores;
                var profile = pool.CurrentPlayer;
                var previousFull = pool.ScoreSync.LastFullSyncUtc;
                Api.FailProfile = profileFailure;
                Api.Fetch = page => page == 1 ? Task.FromResult(Page(1)) : throw new IOException("scores offline");
                calculator.RequestFullScoreSync(Leaderboard.ScoreSaber, "-1", FullScoreSyncReason.Manual);
                Assert.IsFalse(await calculator.SynchronizeScores(Leaderboard.ScoreSaber, "-1", default));
                Assert.AreSame(original, pool.LsScores);
                Assert.AreSame(profile, pool.CurrentPlayer);
                Assert.AreEqual(previousFull, pool.ScoreSync.LastFullSyncUtc);
                Assert.IsTrue(pool.ScoreSync.RetryAfterUtc > DateTime.UtcNow.AddMinutes(14));
                int calls = Api.Calls;
                Assert.IsFalse(await calculator.SynchronizeScores(Leaderboard.ScoreSaber, "-1", default));
                Assert.AreEqual(calls, Api.Calls);
            }
            finally { await calculator.ShutdownAsync(); }
        }

        [TestMethod]
        public async Task GameplayCancellationKeepsRequestWithoutApplyingPartialDataOrBackoff()
        {
            var pool = Pool();
            var calculator = await Create(pool);
            using var gameplay = new CancellationTokenSource();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Api.Fetch = async page =>
            {
                if (page == 1) return Page(page);
                entered.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, NetworkUtil.RequestCancellation);
                return Page(page);
            };
            try
            {
                var original = pool.LsScores;
                calculator.RequestFullScoreSync(Leaderboard.ScoreSaber, "-1", FullScoreSyncReason.Manual);
                var sync = calculator.SynchronizeScores(Leaderboard.ScoreSaber, "-1", gameplay.Token);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                gameplay.Cancel();
                bool canceled = false;
                try { await sync.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (OperationCanceledException) { canceled = true; }
                Assert.IsTrue(canceled);
                Assert.AreSame(original, pool.LsScores);
                Assert.AreEqual(999, pool.CurrentPlayer.Pp);
                Assert.AreEqual(DateTime.MinValue, pool.ScoreSync.RetryAfterUtc);
                Assert.AreEqual(FullScoreSyncReason.Manual, pool.ScoreSync.PendingReason);
                Api.Fetch = page => Task.FromResult(Page(page));
                Assert.IsTrue(await calculator.SynchronizeScores(Leaderboard.ScoreSaber, "-1", default));
            }
            finally { await calculator.ShutdownAsync(); }
        }

        [TestMethod]
        public async Task DifferentLeaderboardsShareOneFullSyncQueue()
        {
            var pool = Pool();
            var calculator = await Create(pool, true);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Api.Fetch = async page => { if (page == 1) { entered.TrySetResult(true); await release.Task; } return Page(page); };
            try
            {
                calculator.RequestFullScoreSync(Leaderboard.ScoreSaber, "-1", FullScoreSyncReason.Manual);
                calculator.RequestFullScoreSync(Leaderboard.BeatLeader, "-1", FullScoreSyncReason.Manual);
                var first = calculator.SynchronizeScores(Leaderboard.ScoreSaber, "-1", default);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var second = calculator.SynchronizeScores(Leaderboard.BeatLeader, "-1", default);
                await Task.Delay(50);
                Assert.IsFalse(BeatApi.Entered.Task.IsCompleted);
                release.TrySetResult(true);
                Assert.IsTrue(await first);
                Assert.IsTrue(await second);
                Assert.IsTrue(BeatApi.Entered.Task.IsCompleted);
            }
            finally { release.TrySetResult(true); await calculator.ShutdownAsync(); }
        }

        [TestMethod]
        public async Task RecreatedCalculatorRetainsPendingScoreAndDoesNotApplyUnconfirmedSnapshot()
        {
            var pool = Pool();
            var previous = await Create(pool);
            PPPMapPool restored;
            try
            {
                previous.RegisterExpectedScore(Leaderboard.ScoreSaber, "-1", new PPPScoreSetData { hash = "OLD_SOLOSTANDARD_9", pp = 250 });
                previous.RequestFullScoreSync(Leaderboard.ScoreSaber, "-1", FullScoreSyncReason.Manual);
                restored = previous.GetSaveData()["ScoreSaber"].DctMapPool["-1"];
                Assert.AreNotSame(pool.ScoreSync.ExpectedScores, restored.ScoreSync.ExpectedScores);
                Assert.AreEqual(250d, restored.ScoreSync.ExpectedScores["old_solostandard_9"].pp);
            }
            finally { await previous.ShutdownAsync(); }

            var calculator = await Create(restored);
            try
            {
                Api.Fetch = page =>
                {
                    Assert.AreEqual(999, restored.CurrentPlayer.Pp, "Profile must not be committed before score confirmation.");
                    Assert.AreEqual(200, restored.LsScores.Single(x => x.Searchstring == "OLD_SOLOSTANDARD_9").Pp);
                    var response = Page(page);
                    if (Api.Calls >= 3 && page == 1) response.playerScores[0].score.pp = 250;
                    return Task.FromResult(response);
                };
                Assert.IsTrue(await calculator.SynchronizeScores(Leaderboard.ScoreSaber, "-1", default));
                Assert.AreEqual(4, Api.Calls, "The first full response does not confirm the expected PP, so it must be retried.");
                Assert.AreEqual(0, restored.ScoreSync.ExpectedScores.Count);
                Assert.AreEqual(250, restored.LsScores.Single(x => x.Searchstring == "OLD_SOLOSTANDARD_9").Pp);
                Assert.AreEqual(500, restored.CurrentPlayer.Pp);
            }
            finally { await calculator.ShutdownAsync(); }
        }
    }
}
