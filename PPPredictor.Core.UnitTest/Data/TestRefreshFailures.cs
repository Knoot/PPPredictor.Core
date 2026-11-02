using System.Net;
using System.Net.Http;
using PPPredictor.Core;
using PPPredictor.Core.API;
using PPPredictor.Core.Calculator;
using PPPredictor.Core.DataType;
using PPPredictor.Core.DataType.MapPool;
using PPPredictor.Core.DataType.Score;
using PPPredictor.Core.UnitTest.MockService;
using static PPPredictor.Core.DataType.Enums;
using static PPPredictor.Core.DataType.LeaderBoard.ScoreSaberDataTypes;

namespace UnitTests.Data
{
    [TestClass]
    public class TestRefreshFailures
    {
        private sealed class Handler : HttpMessageHandler
        {
            internal Func<HttpRequestMessage, HttpResponseMessage> Respond = _ => new HttpResponseMessage(HttpStatusCode.OK);
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(Respond(request));
        }

        private static HttpResponseMessage Json(string json) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };

        [TestMethod]
        public async Task ConfirmedMissingMapIsNotAServerFailure()
        {
            var handler = new Handler { Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound) };
            using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
            Assert.IsNull(await NetworkUtil.GetDataAsync<List<int>>(client, Leaderboard.ScoreSaber, "test", "map", allowNotFound: true));
            handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);
            await Assert.ThrowsExceptionAsync<HttpRequestException>(() => NetworkUtil.GetDataAsync<List<int>>(
                client, Leaderboard.ScoreSaber, "test", "map", allowNotFound: true));
        }

        [TestMethod]
        public async Task AccSaberEmptyCategoriesRecoverWithoutRestart()
        {
            var now = DateTime.UtcNow;
            int calls = 0;
            var handler = new Handler { Respond = _ => { calls++; return Json("[]"); } };
            using var client = new HttpClient(handler);
            var api = new AccSaberApi(client, () => now);
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => api.GetAccSaberMapPools());
            handler.Respond = _ => { calls++; return Json("[{\"id\":\"tech-id\",\"code\":\"TECH\",\"name\":\"Tech\"}]"); };
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => api.GetAccSaberMapPools());
            Assert.AreEqual(1, calls);
            now = now.AddSeconds(15);
            Assert.AreEqual("tech", (await api.GetAccSaberMapPools()).Single().categoryName);
            Assert.AreEqual(2, calls);
        }

        [DataTestMethod]
        [DataRow(401, 1)]
        [DataRow(404, 1)]
        [DataRow(429, 3)]
        [DataRow(503, 3)]
        public async Task RetriesAreBoundedAndOnlyForTransientErrors(int status, int expectedCalls)
        {
            int calls = 0;
            using var client = new HttpClient(new Handler
            {
                Respond = _ => { calls++; return new HttpResponseMessage((HttpStatusCode)status); }
            }) { BaseAddress = new Uri("https://example.test/") };
            await Assert.ThrowsExceptionAsync<HttpRequestException>(() => NetworkUtil.GetDataAsync<List<int>>(client, Leaderboard.ScoreSaber, "test", "scores"));
            Assert.AreEqual(expectedCalls, calls);
        }

        [TestMethod]
        public async Task TransientFailureCanRecoverWithinOneRequest()
        {
            int calls = 0;
            using var client = new HttpClient(new Handler
            {
                Respond = _ => ++calls == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Json("[]")
            }) { BaseAddress = new Uri("https://example.test/") };
            var result = await NetworkUtil.GetDataAsync<List<int>>(client, Leaderboard.ScoreSaber, "test", "scores");
            Assert.AreEqual(2, calls);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public async Task LongRetryAfterDoesNotCauseImmediateRetry()
        {
            int calls = 0;
            using var client = new HttpClient(new Handler
            {
                Respond = _ =>
                {
                    calls++;
                    var response = new HttpResponseMessage((HttpStatusCode)429);
                    response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(1));
                    return response;
                }
            }) { BaseAddress = new Uri("https://example.test/") };
            await Assert.ThrowsExceptionAsync<HttpRequestException>(() => NetworkUtil.GetDataAsync<List<int>>(client, Leaderboard.ScoreSaber, "test", "scores"));
            Assert.AreEqual(1, calls);
        }

        [TestMethod]
        public async Task HttpFailureIsNotAnEmptySuccessAndNextRequestCanRecover()
        {
            var handler = new Handler { Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized) };
            using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
            await Assert.ThrowsExceptionAsync<HttpRequestException>(() => NetworkUtil.GetDataAsync<List<int>>(client, Leaderboard.ScoreSaber, "test", "scores"));
            handler.Respond = _ => Json("[]");
            var scores = await NetworkUtil.GetDataAsync<List<int>>(client, Leaderboard.ScoreSaber, "test", "scores");
            Assert.AreEqual(0, scores.Count);
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("null")]
        [DataRow("not json")]
        public async Task InvalidResponseIsNotAnEmptySuccess(string body)
        {
            using var client = new HttpClient(new Handler { Respond = _ => Json(body) }) { BaseAddress = new Uri("https://example.test/") };
            Exception? failure = null;
            try
            {
                await NetworkUtil.GetDataAsync<List<int>>(client, Leaderboard.ScoreSaber, "test", "scores");
            }
            catch (Newtonsoft.Json.JsonException ex) { failure = ex; }
            Assert.IsNotNull(failure);
        }

        [TestMethod]
        public async Task MissingProfilePpIsNotAZeroProfile()
        {
            using var client = new HttpClient(new Handler { Respond = _ => Json("{}") }) { BaseAddress = new Uri("https://example.test/") };
            await Assert.ThrowsExceptionAsync<Newtonsoft.Json.JsonSerializationException>(() =>
                NetworkUtil.GetDataAsync<ScoreSaberPlayer>(client, Leaderboard.ScoreSaber, "test", "player"));
        }

        [TestMethod]
        public void MissingScoreMetadataIsNotAnEmptyPage()
        {
            Assert.ThrowsException<InvalidOperationException>(() => new PPPScoreCollection(new ScoreSaberPlayerScoreList
            {
                metadata = new ScoreSaberScoreListMetadata(), playerScores = new List<ScoreSaberPlayerScore>()
            }));
        }

        [TestMethod]
        public async Task AccSaberDoesNotReturnFirstPageWhenNextPageFails()
        {
            using var client = new HttpClient(new Handler
            {
                Respond = request => request.RequestUri!.Query.Contains("page=0")
                    ? Json("{\"content\":[{\"songHash\":\"HASH\",\"ap\":100}],\"last\":false}")
                    : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            });
            var api = new AccSaberApi(client);
            await Assert.ThrowsExceptionAsync<HttpRequestException>(() => api.GetAllScores("123"));
        }

        private sealed class Calculator : PPCalculatorScoreSaber<MockScoreSaberApi>
        {
            internal Func<int, PPPScoreCollection> Fetch = _ => Page(1, 0);
            internal Func<PPPPlayer> Profile = () => new PPPPlayer();
            internal Calculator(PPPMapPool pool, Settings settings) : base(new Dictionary<string, PPPMapPool> { [pool.Id] = pool }, settings, _ => new PPPBeatMapInfo()) { }
            internal override Task<PPPScoreCollection> GetRecentScores(string userId, int pageSize, int page, PPPMapPool mapPool) => Task.FromResult(Fetch(page));
            internal override Task<PPPPlayer> GetPlayerInfo(long userId, PPPMapPool mapPool) => Task.FromResult(Profile());
        }

        private static Settings Settings() => new Settings(true, false, false, false, "123", PPGainCalculationType.Weighted,
            MapPoolSorting.Alphabetical, "", -7, DateTime.Now.AddHours(-1), 12);

        private static PPPScoreCollection Page(int page, int total, string? hash = null) => new PPPScoreCollection(new ScoreSaberPlayerScoreList
        {
            metadata = new ScoreSaberScoreListMetadata { page = page, itemsPerPage = 1, total = total },
            playerScores = hash == null ? new List<ScoreSaberPlayerScore>() : new List<ScoreSaberPlayerScore>
            {
                new ScoreSaberPlayerScore
                {
                    score = new ScoreSaberScore { pp = 100, timeSet = DateTimeOffset.UtcNow },
                    leaderboard = new ScoreSaberLeaderboardInfo
                    {
                        songHash = hash,
                        difficulty = new ScoreSaberDifficulty { gameMode = "SoloStandard", difficulty = 9 }
                    }
                }
            }
        });

        [DataTestMethod]
        [DataRow(false, 1)]
        [DataRow(false, 2)]
        [DataRow(true, 1)]
        [DataRow(true, 2)]
        public async Task FailedPageDoesNotCommitPartialScores(bool hasCache, int failedPage)
        {
            var pool = new PPPMapPool();
            if (hasCache) pool.LsScores.Add(new ShortScore("OLD_SOLOSTANDARD_9", 200));
            var calculator = new Calculator(pool, Settings());
            var originalList = pool.LsScores;
            var originalTimestamp = pool.DtLastScoreSet;
            double originalSum = pool.LsScores.LastOrDefault()?.WeightedSum ?? 0;
            calculator.Fetch = page => page == failedPage ? PPPScoreCollection.Failed() : Page(page, 2, "NEW" + page);

            await calculator.GetPlayerScores(pool, 1, 1);
            Assert.AreSame(originalList, pool.LsScores);
            Assert.AreEqual(hasCache ? 1 : 0, pool.LsScores.Count);
            Assert.AreEqual(originalTimestamp, pool.DtLastScoreSet);
            Assert.AreEqual(originalSum, pool.LsScores.LastOrDefault()?.WeightedSum ?? 0);
            Assert.AreEqual(hasCache, pool.HasScoreDataFetched);
            Assert.IsTrue(pool.ScoreRefreshFailed);

            calculator.Fetch = page => Page(page, 2, "NEW" + page);
            await calculator.GetPlayerScores(pool, 1, 1);
            Assert.AreEqual(hasCache ? 3 : 2, pool.LsScores.Count);
            Assert.IsTrue(pool.HasScoreDataFetched);
            Assert.IsFalse(pool.ScoreRefreshFailed);
        }

        [TestMethod]
        public async Task SuccessfulEmptyScoresAreLoaded()
        {
            var pool = new PPPMapPool();
            var calculator = new Calculator(pool, Settings());
            await calculator.GetPlayerScores(pool, 1, 1);
            Assert.IsTrue(pool.HasScoreDataFetched);
            Assert.IsFalse(pool.ScoreRefreshFailed);
            Assert.AreEqual(0, pool.LsScores.Count);
        }

        [TestMethod]
        public async Task ForcedFullRefreshAcceptsDecreasedAndDeletedScoresDespiteOldTimestamps()
        {
            var pool = new PPPMapPool { HasScoreDataFetched = true, ScoresRequireRefresh = true,
                DtLastScoreSet = DateTimeOffset.UtcNow.AddDays(1) };
            pool.LsScores.Add(new ShortScore("OLD_SOLOSTANDARD_9", 200));
            pool.LsScores.Add(new ShortScore("DELETED_SOLOSTANDARD_9", 300));
            var calculator = new Calculator(pool, Settings());
            int pages = 0;
            calculator.Fetch = page => { pages++; return Page(page, 2, page == 1 ? "OLD" : "NEW"); };
            Assert.IsTrue(await calculator.GetPlayerScores(pool, 1, 1, fetchOnePage: true, forceFullRefresh: true));
            Assert.AreEqual(2, pages);
            Assert.AreEqual(2, pool.LsScores.Count);
            Assert.AreEqual(100, pool.LsScores.Single(x => x.Searchstring == "OLD_SOLOSTANDARD_9").Pp);
            Assert.IsFalse(pool.LsScores.Any(x => x.Searchstring == "DELETED_SOLOSTANDARD_9"));
            Assert.IsFalse(pool.ScoresRequireRefresh);
        }

        [TestMethod]
        public async Task FailedFullRefreshRetainsScoresAndInvalidationAcrossSave()
        {
            var pool = new PPPMapPool { ScoresRequireRefresh = true };
            pool.CurveInfo = new PPPredictor.Core.DataType.Curve.CurveInfo(CurveType.ScoreSaber);
            pool.LsScores.Add(new ShortScore("OLD_SOLOSTANDARD_9", 200));
            var calculator = new Calculator(pool, Settings()) { Fetch = _ => PPPScoreCollection.Failed() };
            Assert.IsFalse(await calculator.GetPlayerScores(pool, 1, 1, forceFullRefresh: true));
            Assert.AreEqual(200, pool.LsScores.Single().Pp);
            Assert.IsTrue(pool.ScoresRequireRefresh);
            var saved = Newtonsoft.Json.JsonConvert.SerializeObject(pool);
            Assert.IsTrue(Newtonsoft.Json.JsonConvert.DeserializeObject<PPPMapPool>(saved)!.ScoresRequireRefresh);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task FailedProfilePreservesPlayerSessionAndResetTime(bool throws)
        {
            var current = new PPPPlayer { Pp = 500, Rank = 100 };
            var session = new PPPPlayer { Pp = 400, Rank = 110 };
            var pool = new PPPMapPool { CurrentPlayer = current, SessionPlayer = session };
            var settings = Settings();
            var resetTime = settings.LastSessionReset;
            var calculator = new Calculator(pool, settings) { Profile = () => throws ? throw new HttpRequestException("offline") : new PPPPlayer(true) };
            await calculator.UpdatePlayer(pool, true);
            Assert.AreSame(current, pool.CurrentPlayer);
            Assert.AreSame(session, pool.SessionPlayer);
            Assert.AreEqual(resetTime, settings.LastSessionReset);
            Assert.IsTrue(pool.ProfileRefreshFailed);

            calculator.Profile = () => new PPPPlayer { Pp = 600 };
            await calculator.UpdatePlayer(pool, false);
            Assert.AreEqual(600d, pool.CurrentPlayer.Pp);
            Assert.AreSame(session, pool.SessionPlayer);
            Assert.IsFalse(pool.ProfileRefreshFailed);
        }

        [TestMethod]
        public async Task SuccessfulZeroProfileIsNotAnError()
        {
            var pool = new PPPMapPool { CurrentPlayer = new PPPPlayer { Pp = 100 } };
            await new Calculator(pool, Settings()).UpdatePlayer(pool, false);
            Assert.AreEqual(0d, pool.CurrentPlayer.Pp);
            Assert.IsFalse(pool.ProfileRefreshFailed);
            Assert.AreSame(pool.CurrentPlayer, pool.SessionPlayer);
        }
    }
}
