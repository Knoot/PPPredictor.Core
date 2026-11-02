using PPPredictor.Core.API;
using PPPredictor.Core.Calculator;
using PPPredictor.Core.DataType;
using PPPredictor.Core.DataType.BeatSaberEncapsulation;
using PPPredictor.Core.DataType.Curve;
using PPPredictor.Core.DataType.MapPool;
using PPPredictor.Core.UnitTest.MockService;
using PPPredictor.Core.Interface;
using PPPredictor.Core.DataType.LeaderBoard;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static PPPredictor.Core.DataType.Enums;

namespace PPPredictor.Core.UnitTest.LeaderBoard
{
    [TestClass]
    public class TestScoreSaber
    {
        [TestMethod]
        [ExpectedException(typeof(Exception))]
        public async Task InstanceCreationErrorWithoutLookup()
        {
            CalculatorInstance ci = await CalculatorInstance.CreateAsyncMock<MockScoreSaberApi, MockBeatLeader, MockHitBloq, MockAccSaber>(
                new Settings(true, false, false, false, "123", DataType.Enums.PPGainCalculationType.Raw, DataType.Enums.MapPoolSorting.Alphabetical, "", 7, DateTime.Now, 12),
                new Dictionary<string, DataType.LeaderBoard.LeaderboardData>(),
                null
                );
        }

        [TestMethod]
        public async Task InstanceCreationWithLookup()
        {
            CalculatorInstance ci = await CalculatorInstance.CreateAsyncMock<MockScoreSaberApi, MockBeatLeader, MockHitBloq, MockAccSaber>(
                new Settings(true, false, false, false, "123", DataType.Enums.PPGainCalculationType.Raw, DataType.Enums.MapPoolSorting.Alphabetical, "", 7, DateTime.Now, 12),
                new Dictionary<string, DataType.LeaderBoard.LeaderboardData>(),
                (PPPBeatMapInfo) => new PPPBeatMapInfo()
                );
        }

        [TestMethod]
        public async Task TestPPCalculation()
        {
            CalculatorInstance ci = await CalculatorInstance.CreateAsyncMock<MockScoreSaberApi, MockBeatLeader, MockHitBloq, MockAccSaber>(
                new Settings(true, false, false, false, "123", DataType.Enums.PPGainCalculationType.Raw, DataType.Enums.MapPoolSorting.Alphabetical, "", 7, DateTime.Now, 12),
                new Dictionary<string, DataType.LeaderBoard.LeaderboardData>(),
                (PPPBeatMapInfo) => new PPPBeatMapInfo()
                );
            PPPBeatMapInfo info = new PPPBeatMapInfo(new PPPBeatMapInfo(), new PPPStarRating(10.18));
            double pp = ci.CalculatePPatPercentage(Enums.Leaderboard.ScoreSaber, "-1", info, 95, false, false);
            Assert.IsTrue(pp < 428.8 && pp > 428.6, "PP Range does not match");
            double ppFailed = ci.CalculatePPatPercentage(Enums.Leaderboard.ScoreSaber, "-1", info, 95, true, false);
            Assert.IsTrue(ppFailed < 61.9 && ppFailed > 61.8, "ppFailed Range does not match");
            double ppPaused = ci.CalculatePPatPercentage(Enums.Leaderboard.ScoreSaber, "-1", info, 95, false, true);
            Assert.IsTrue(ppPaused < 428.8 && ppPaused > 428.6, "PPPaused Range does not match");
            double MaxPp = ci.CalculateMaxPP(Enums.Leaderboard.ScoreSaber, "-1", info);
            Assert.IsTrue(MaxPp < 2301.3 && MaxPp > 2301.2, "MaxPp Range does not match");
        }

        [TestMethod]
        public async Task TestGetBeatMapInfoAsync()
        {
            List<PPPBeatMapInfo>? lsBeatMapInfos = new List<PPPBeatMapInfo>();
            CalculatorInstance ci = await CalculatorInstance.CreateAsyncMock<MockScoreSaberApi, MockBeatLeader, MockHitBloq, MockAccSaber>(
                new Settings(true, false, false, false, "123", DataType.Enums.PPGainCalculationType.Raw, DataType.Enums.MapPoolSorting.Alphabetical, "", 7, DateTime.Now, 12),
                new Dictionary<string, DataType.LeaderBoard.LeaderboardData>(),
                (PPPBeatMapInfo x) =>
                {
                    return lsBeatMapInfos.FirstOrDefault(y => y.CustomLevelHash == x.CustomLevelHash);
                }
                );

            var key = new BeatmapKey { serializedName = "Standard", difficulty = BeatMapDifficulty.ExpertPlus };
            lsBeatMapInfos.Add(new PPPBeatMapInfo(new PPPBeatMapInfo("HASH", key), new PPPStarRating(10)));

            PPPBeatMapInfo returnInfo = await ci.GetBeatMapInfoAsync(Enums.Leaderboard.ScoreSaber, "-1", new PPPBeatMapInfo("HASH", key));
            Assert.IsNotNull(returnInfo);
            Assert.AreEqual(10, returnInfo.ModifiedStarRating.Stars);
            returnInfo = ci.ApplyModifiersToBeatmapInfo(Enums.Leaderboard.ScoreSaber, "-1", returnInfo, new GameplayModifiers(), true, true);
            Assert.AreEqual(10, returnInfo.ModifiedStarRating.Stars);
            returnInfo.OldDotsEnabled = true;
            returnInfo = ci.ApplyModifiersToBeatmapInfo(Enums.Leaderboard.ScoreSaber, "-1", returnInfo, new GameplayModifiers(), true, true);
            Assert.AreEqual(0, returnInfo.ModifiedStarRating.Stars);

            returnInfo = await ci.GetBeatMapInfoAsync(Enums.Leaderboard.ScoreSaber, "-1", new PPPBeatMapInfo("", new BeatmapKey()));
            Assert.AreEqual(0, returnInfo.ModifiedStarRating.Stars);

            lsBeatMapInfos = null;
            returnInfo = await ci.GetBeatMapInfoAsync(Enums.Leaderboard.ScoreSaber, "-1", new PPPBeatMapInfo("HASH", key));
            Assert.AreEqual(-1, returnInfo.ModifiedStarRating.Stars);
        }

        [TestMethod]
        public async Task TestUpdateMapPoolDetails()
        {
            CalculatorInstance ci = await CalculatorInstance.CreateAsyncMock<MockScoreSaberApi, MockBeatLeader, MockHitBloq, MockAccSaber>(
                new Settings(true, false, false, false, "123", DataType.Enums.PPGainCalculationType.Raw, DataType.Enums.MapPoolSorting.Alphabetical, "", 7, DateTime.Now, 12),
                new Dictionary<string, DataType.LeaderBoard.LeaderboardData>(),
                (PPPBeatMapInfo x) => new PPPBeatMapInfo()
                );
            await ci.UpdateMapPoolDetails(Enums.Leaderboard.ScoreSaber, "-1");
        }

        [TestMethod]
        public async Task TestIsScoreSetOnCurrentMapPool()
        {
            CalculatorInstance ci = await CalculatorInstance.CreateAsyncMock<MockScoreSaberApi, MockBeatLeader, MockHitBloq, MockAccSaber>(
                new Settings(true, false, false, false, "123", DataType.Enums.PPGainCalculationType.Raw, DataType.Enums.MapPoolSorting.Alphabetical, "", 7, DateTime.Now, 12),
                new Dictionary<string, DataType.LeaderBoard.LeaderboardData>(),
                (PPPBeatMapInfo x) => new PPPBeatMapInfo()
                );
            bool isScoreSet = ci.IsScoreSetOnCurrentMapPool(Enums.Leaderboard.ScoreSaber, "-1", new PPPScoreSetData());
            Assert.IsTrue(isScoreSet);
        }

        [TestMethod]
        public async Task TestGetMapPools()
        {
            CalculatorInstance ci = await CalculatorInstance.CreateAsyncMock<MockScoreSaberApi, MockBeatLeader, MockHitBloq, MockAccSaber>(
                new Settings(true, false, false, false, "123", DataType.Enums.PPGainCalculationType.Raw, DataType.Enums.MapPoolSorting.Alphabetical, "", 7, DateTime.Now, 12),
                new Dictionary<string, DataType.LeaderBoard.LeaderboardData>(),
                (PPPBeatMapInfo x) => new PPPBeatMapInfo()
                );
            List<PPPMapPoolShort> lsPools = ci.GetMapPools(Enums.Leaderboard.ScoreSaber);
            Assert.AreEqual(1, lsPools.Count);
        }

        [TestMethod]
        public async Task TestGetPlayerScorePPGain()
        {
            Dictionary<string, PPPMapPool> dctMapPool = new Dictionary<string, PPPMapPool>();
            dctMapPool.Add("-1", new PPPMapPool(MapPoolType.Default, $"", PPCalculatorScoreSaber<ScoresaberAPI>.accumulationConstant, 0, CurveParser.ParseToCurve(new CurveInfo(CurveType.ScoreSaber))));
            dctMapPool["-1"].LsScores = new List<DataType.Score.ShortScore>();
            for (int i = 0; i < 100; i++)
            {
                dctMapPool["-1"].LsScores.Add(new DataType.Score.ShortScore($"HASH_{i}", 300 - i));

            };
            dctMapPool["-1"].LsScores = dctMapPool["-1"].LsScores; //Trigger sorting like when loading from file or adding score
            dctMapPool["-1"].CurrentPlayer.Pp = 7643.94898356585;

            Dictionary<string, DataType.LeaderBoard.LeaderboardData> dct = new Dictionary<string, DataType.LeaderBoard.LeaderboardData>();
            dct.Add(Enums.Leaderboard.ScoreSaber.ToString(), new DataType.LeaderBoard.LeaderboardData() { DctMapPool = dctMapPool });

            List<PPPBeatMapInfo> lsBeatMapInfos = new List<PPPBeatMapInfo>();
            CalculatorInstance ci = await CalculatorInstance.CreateAsyncMock<MockScoreSaberApi, MockBeatLeader, MockHitBloq, MockAccSaber>(
                new Settings(true, false, false, false, "123", DataType.Enums.PPGainCalculationType.Raw, DataType.Enums.MapPoolSorting.Alphabetical, "", 7, DateTime.Now, 12),
                new Dictionary<string, DataType.LeaderBoard.LeaderboardData>(),
                (PPPBeatMapInfo x) =>
                {
                    return lsBeatMapInfos.FirstOrDefault(y => y.CustomLevelHash == x.CustomLevelHash);
                }
                );

            lsBeatMapInfos.Add(new PPPBeatMapInfo(new PPPBeatMapInfo("HASH", new BeatmapKey()), new PPPStarRating(10)));

            PPGainResult gain = ci.GetPlayerScorePPGain(Enums.Leaderboard.ScoreSaber, "-1", "HASH01", 100);
            Assert.IsFalse(ci.HasScoreDataForPPGain(Leaderboard.ScoreSaber, "-1"));
            Assert.AreEqual(0d, gain.PpGainWeighted, "An unloaded profile must not be treated as an empty profile.");
            Assert.AreEqual(0d, gain.PpGainRaw);

            ci = await CalculatorInstance.CreateAsyncMock<MockScoreSaberApi, MockBeatLeader, MockHitBloq, MockAccSaber>(
                new Settings(true, false, false, false, "123", DataType.Enums.PPGainCalculationType.Raw, DataType.Enums.MapPoolSorting.Alphabetical, "", 7, DateTime.Now, 12),
                dct,
                (PPPBeatMapInfo x) =>
                {
                    return lsBeatMapInfos.FirstOrDefault(y => y.CustomLevelHash == x.CustomLevelHash);
                }
                );

            gain = ci.GetPlayerScorePPGain(Enums.Leaderboard.ScoreSaber, "-1", "HASH01", 100);
            Assert.AreNotEqual(gain.PpGainWeighted, 100);

            //Improve score
            gain = ci.GetPlayerScorePPGain(Enums.Leaderboard.ScoreSaber, "-1", "HASH_10", 299);
            Assert.AreEqual(7.30, gain.PpGainWeighted, 0.001);

            //Improving best score
            gain = ci.GetPlayerScorePPGain(Enums.Leaderboard.ScoreSaber, "-1", "HASH_0", 333);
            Assert.AreEqual(gain.PpGainWeighted, 33);

            //failed score
            gain = ci.GetPlayerScorePPGain(Enums.Leaderboard.ScoreSaber, "-1", "HASH_0", 0);
            Assert.AreEqual(gain.PpGainWeighted, 0);
            Assert.AreEqual(gain.PpGainRaw, -300);
            gain = ci.GetPlayerScorePPGain(Enums.Leaderboard.ScoreSaber, "-1", "HASH_NOTEXISINT", 0);
            Assert.AreEqual(gain.PpGainWeighted, 0);
            Assert.AreEqual(gain.PpGainRaw, 0);

            //no improvement
            gain = ci.GetPlayerScorePPGain(Enums.Leaderboard.ScoreSaber, "-1", "HASH_0", 200);
            Assert.AreEqual(gain.PpGainWeighted, 0);
            Assert.AreEqual(gain.PpGainRaw, -100);

            dctMapPool["-1"].CurrentPlayer.Pp = 9000;
            gain = ci.GetPlayerScorePPGain(Enums.Leaderboard.ScoreSaber, "-1", "HASH_10", 299);
            Assert.AreEqual(9007.30, gain.PpTotal, 0.001);
            Assert.AreEqual(7.30, gain.PpGainWeighted, 0.001);

            dctMapPool["-1"].LsScores = null;
            ci = await CalculatorInstance.CreateAsyncMock<MockScoreSaberApi, MockBeatLeader, MockHitBloq, MockAccSaber>(
                new Settings(true, false, false, false, "123", DataType.Enums.PPGainCalculationType.Raw, DataType.Enums.MapPoolSorting.Alphabetical, "", 7, DateTime.Now, 12),
                dct,
                (PPPBeatMapInfo x) =>
                {
                    return lsBeatMapInfos.FirstOrDefault(y => y.CustomLevelHash == x.CustomLevelHash);
                }
                );
            // Invalid cached data must preserve the known total without fabricating a gain.
            gain = ci.GetPlayerScorePPGain(Enums.Leaderboard.ScoreSaber, "-1", "HASH_0", 200);
            Assert.AreEqual(9000d, gain.PpTotal);
            Assert.AreEqual(0d, gain.PpGainWeighted);
            Assert.AreEqual(0d, gain.PpGainRaw);
        }

        private sealed class EmptyScoreSaberApi : IScoresaberAPI
        {
            public Task<ScoreSaberDataTypes.ScoreSaberLeaderboardInfo> GetLeaderboardInfo(string hash, int difficulty, string gameMode)
                => throw new NotSupportedException();
            public Task<ScoreSaberDataTypes.ScoreSaberPlayer> GetPlayer(long playerId)
                => Task.FromResult(new ScoreSaberDataTypes.ScoreSaberPlayer { pp = 0 });

            public Task<ScoreSaberDataTypes.ScoreSaberPlayerList> GetPlayers(double? page)
                => Task.FromResult(new ScoreSaberDataTypes.ScoreSaberPlayerList());

            public Task<ScoreSaberDataTypes.ScoreSaberPlayerScoreList> GetPlayerScores(string playerId, int? limit, int? page)
                => Task.FromResult(new ScoreSaberDataTypes.ScoreSaberPlayerScoreList
                {
                    metadata = new ScoreSaberDataTypes.ScoreSaberScoreListMetadata
                    {
                        page = page.GetValueOrDefault(1), itemsPerPage = limit.GetValueOrDefault(10), total = 0
                    },
                    playerScores = new List<ScoreSaberDataTypes.ScoreSaberPlayerScore>()
                });
        }

        [TestMethod]
        public async Task FirstScoreHasFullWeightOnlyAfterSuccessfulEmptyProfileLoad()
        {
            var ci = await CalculatorInstance.CreateAsyncMock<EmptyScoreSaberApi, MockBeatLeader, MockHitBloq, MockAccSaber>(
                new Settings(true, false, false, false, "123", PPGainCalculationType.Raw, MapPoolSorting.Alphabetical,
                    "", -7, DateTime.Now.AddHours(-1), 12),
                new Dictionary<string, LeaderboardData>(), _ => new PPPBeatMapInfo());

            Assert.IsFalse(ci.HasScoreDataForPPGain(Leaderboard.ScoreSaber, "-1"));
            Assert.AreEqual(0d, ci.GetPlayerScorePPGain(Leaderboard.ScoreSaber, "-1", "HASH01", 100).PpGainWeighted);

            var (_, player) = await ci.UpdatePlayer(Leaderboard.ScoreSaber, "-1", false);
            Assert.IsFalse(player.IsErrorUser);
            Assert.AreEqual(0d, player.Pp);
            Assert.IsTrue(await ci.GetPlayerScores(Leaderboard.ScoreSaber, "-1", 10, 100));
            Assert.IsTrue(ci.HasScoreDataForPPGain(Leaderboard.ScoreSaber, "-1"));
            Assert.IsFalse(ci.HasDataRefreshFailed(Leaderboard.ScoreSaber, "-1"));

            var gain = ci.GetPlayerScorePPGain(Leaderboard.ScoreSaber, "-1", "HASH01", 100);
            Assert.AreEqual(100d, gain.PpGainWeighted);
            Assert.AreEqual(100d, gain.PpGainRaw);
            Assert.AreEqual(100d, gain.PpTotal);
        }

        [TestMethod]
        public async Task TestUpdatePlayer()
        {
            Dictionary<string, PPPMapPool> dctMapPool = new Dictionary<string, PPPMapPool>();
            dctMapPool.Add("-1", new PPPMapPool(MapPoolType.Default, $"", PPCalculatorScoreSaber<ScoresaberAPI>.accumulationConstant, 0, CurveParser.ParseToCurve(new CurveInfo(CurveType.ScoreSaber))));
            PPPPlayer player = new PPPPlayer()
            {
                Country = "DE",
                CountryRank = 200,
                Rank = 2100,
                Pp = 120
            };
            dctMapPool["-1"].CurrentPlayer = player;
            dctMapPool["-1"].SessionPlayer = player;
            dctMapPool["-1"].DtUtcLastRefresh = DateTime.Now;

            Dictionary<string, DataType.LeaderBoard.LeaderboardData> dct = new Dictionary<string, DataType.LeaderBoard.LeaderboardData>();
            dct.Add(Enums.Leaderboard.ScoreSaber.ToString(), new DataType.LeaderBoard.LeaderboardData() { DctMapPool = dctMapPool });

            CalculatorInstance ci = await CalculatorInstance.CreateAsyncMock<MockScoreSaberApi, MockBeatLeader, MockHitBloq, MockAccSaber>(
                new Settings(true, false, false, false, "123", DataType.Enums.PPGainCalculationType.Raw, DataType.Enums.MapPoolSorting.Alphabetical, "", 7, DateTime.Now.AddHours(-1), 12),
                dct,
                (PPPBeatMapInfo x) => new PPPBeatMapInfo()
            );

            (PPPPlayer sessionPlayer, PPPPlayer currentPlayer) = await ci.UpdatePlayer(Leaderboard.ScoreSaber, "-1", false);
            Assert.AreEqual(2100, sessionPlayer.Rank);
            Assert.AreEqual(2000, currentPlayer.Rank);

            (sessionPlayer, currentPlayer) = await ci.UpdatePlayer(Leaderboard.ScoreSaber, "-1", true);
            Assert.AreEqual(2000, sessionPlayer.Rank);
            Assert.AreEqual(2000, currentPlayer.Rank);

            ci = await CalculatorInstance.CreateAsyncMock<MockScoreSaberApi, MockBeatLeader, MockHitBloq, MockAccSaber>(
                new Settings(true, false, false, false, "123", DataType.Enums.PPGainCalculationType.Raw, DataType.Enums.MapPoolSorting.Alphabetical, "", 7, DateTime.Now, 12),
                dct,
                (PPPBeatMapInfo x) => new PPPBeatMapInfo()
            );

            (sessionPlayer, currentPlayer) = await ci.UpdatePlayer(Leaderboard.ScoreSaber, "-1", false);
            Assert.AreEqual(2000, sessionPlayer.Rank);
            Assert.AreEqual(2000, currentPlayer.Rank);
        }

        [TestMethod]
        public async Task TestRankGain()
        {
            Dictionary<string, PPPMapPool> dctMapPool = new Dictionary<string, PPPMapPool>();
            dctMapPool.Add("-1", new PPPMapPool(MapPoolType.Default, $"", PPCalculatorScoreSaber<ScoresaberAPI>.accumulationConstant, 0, CurveParser.ParseToCurve(new CurveInfo(CurveType.ScoreSaber))));
            PPPPlayer player = new PPPPlayer()
            {
                Country = "DE",
                CountryRank = 200,
                Rank = 2100,
                Pp = 11000
            };
            dctMapPool["-1"].CurrentPlayer = player;
            dctMapPool["-1"].SessionPlayer = player;
            dctMapPool["-1"].DtUtcLastRefresh = DateTime.Now;

            Dictionary<string, DataType.LeaderBoard.LeaderboardData> dct = new Dictionary<string, DataType.LeaderBoard.LeaderboardData>();
            dct.Add(Enums.Leaderboard.ScoreSaber.ToString(), new DataType.LeaderBoard.LeaderboardData() { DctMapPool = dctMapPool });

            CalculatorInstance ci = await CalculatorInstance.CreateAsyncMock<MockScoreSaberApi, MockBeatLeader, MockHitBloq, MockAccSaber>(
                new Settings(true, false, false, false, "123", DataType.Enums.PPGainCalculationType.Raw, DataType.Enums.MapPoolSorting.Alphabetical, "", 7, DateTime.Now.AddHours(-1), 12),
                dct,
                (PPPBeatMapInfo x) => new PPPBeatMapInfo()
            );

            RankGainResult gain = await ci.GetPlayerRankGain(Leaderboard.ScoreSaber, "-1", 11000);
            Assert.AreEqual(0, gain.RankGainGlobal);
            gain = await ci.GetPlayerRankGain(Leaderboard.ScoreSaber, "-1", 10000);
            Assert.AreEqual(0, gain.RankGainGlobal);
            gain = await ci.GetPlayerRankGain(Leaderboard.ScoreSaber, "-1", 12000);
            // Rank 2000 belongs to page 40, not 41; the five-page limit ends at rank 1751.
            Assert.AreEqual(249, gain.RankGainGlobal);
            gain = await ci.GetPlayerRankGain(Leaderboard.ScoreSaber, "-1", 30000);
            Assert.AreEqual(449, gain.RankGainGlobal);
            Assert.IsTrue(gain.IsRankGainCanceledByLimit);
        }

    }
}
