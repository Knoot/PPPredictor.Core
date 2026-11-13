using PPPredictor.Core.DataType.LeaderBoard;
using static PPPredictor.Core.DataType.Enums;

namespace UnitTests.Data
{
    [TestClass]
    public class TestPPPLeaderboardInfo
    {
        [TestMethod]
        public void DefaultConstructor()
        {
            PPPLeaderboardInfo leaderboardInfo = new PPPLeaderboardInfo();
            Assert.IsNull(leaderboardInfo.LeaderboardName);
            Assert.IsNull(leaderboardInfo.LeaderboardIcon);
            Assert.IsNull(leaderboardInfo.PpSuffix);
            Assert.AreEqual(leaderboardInfo.LeaderboardFirstPageIndex, 0);
            Assert.AreEqual(leaderboardInfo.IsCountryRankEnabled, false);
            Assert.AreEqual(leaderboardInfo.LargePageSize, 0, "LargePageSize should be 10");
            Assert.AreEqual(leaderboardInfo.PlayerPerPages, 0, "PlayerPerPages should be 10");
            Assert.IsFalse(leaderboardInfo.HasGetAllScoresFunctionality, "HasGetAllScoresFunctionality should be false");
            Assert.IsTrue(leaderboardInfo.HasGetRecentScoresFunctionality, "HasGetRecentScoresFunctionality should be true");
            Assert.IsFalse(leaderboardInfo.HasPPToRankFunctionality, "HasPPToRankFunctionality should be false");
            Assert.AreEqual(leaderboardInfo.TaskDelayValue, 250, "TaskDelayValue should be 250");
            Assert.IsTrue(leaderboardInfo.HasOldDotRanking, "HasOldDotRanking should be true");
            Assert.AreEqual(leaderboardInfo.PageFetchLimit, 5, "LargePageSize should be 5");
        }

        [TestMethod]
        public void ScoreSaberConstructor()
        {
            PPPLeaderboardInfo leaderboardInfo = new PPPLeaderboardInfo(Leaderboard.ScoreSaber);
            Assert.AreEqual(leaderboardInfo.LeaderboardName, Leaderboard.ScoreSaber.ToString());
            Assert.AreEqual(leaderboardInfo.PpSuffix, "pp");
            Assert.AreEqual(leaderboardInfo.LeaderboardFirstPageIndex, 1);
            Assert.IsTrue(leaderboardInfo.IsCountryRankEnabled);
            Assert.IsNotNull(leaderboardInfo.LeaderboardIcon);
            Assert.AreEqual(leaderboardInfo.LargePageSize, 100, "Full score sync should use 100 results per page.");
            Assert.AreEqual(leaderboardInfo.PlayerPerPages, 50, "PlayerPerPages should be 10");
            Assert.IsFalse(leaderboardInfo.HasGetAllScoresFunctionality, "HasGetAllScoresFunctionality should be false");
            Assert.IsTrue(leaderboardInfo.HasGetRecentScoresFunctionality, "HasGetRecentScoresFunctionality should be true");
            Assert.IsFalse(leaderboardInfo.HasPPToRankFunctionality, "HasPPToRankFunctionality should be false");
            Assert.AreEqual(leaderboardInfo.TaskDelayValue, 250, "TaskDelayValue should be 250");
            Assert.IsFalse(leaderboardInfo.HasOldDotRanking, "HasOldDotRanking should be true");
            Assert.AreEqual(leaderboardInfo.PageFetchLimit, 5, "LargePageSize should be 5");
        }

        [TestMethod]
        public void BeatLeaderConstructor()
        {
            PPPLeaderboardInfo leaderboardInfo = new PPPLeaderboardInfo(Leaderboard.BeatLeader);
            Assert.AreEqual(leaderboardInfo.LeaderboardName, Leaderboard.BeatLeader.ToString());
            Assert.AreEqual(leaderboardInfo.PpSuffix, "pp");
            Assert.AreEqual(leaderboardInfo.LeaderboardFirstPageIndex, 1);
            Assert.IsTrue(leaderboardInfo.IsCountryRankEnabled);
            Assert.IsNotNull(leaderboardInfo.LeaderboardIcon);
            Assert.AreEqual(leaderboardInfo.LargePageSize, 100, "LargePageSize should be 10");
            Assert.AreEqual(leaderboardInfo.PlayerPerPages, 50, "PlayerPerPages should be 10");
            Assert.IsFalse(leaderboardInfo.HasGetAllScoresFunctionality, "HasGetAllScoresFunctionality should be false");
            Assert.IsTrue(leaderboardInfo.HasGetRecentScoresFunctionality, "HasGetRecentScoresFunctionality should be true");
            Assert.IsFalse(leaderboardInfo.HasPPToRankFunctionality, "HasPPToRankFunctionality should be false");
            Assert.AreEqual(leaderboardInfo.TaskDelayValue, 1100, "TaskDelayValue should be 250");
            Assert.IsTrue(leaderboardInfo.HasOldDotRanking, "HasOldDotRanking should be true");
            Assert.AreEqual(leaderboardInfo.PageFetchLimit, 5, "LargePageSize should be 5");
        }

        [TestMethod]
        public void NoLeaderboardConstructor()
        {
            PPPLeaderboardInfo leaderboardInfo = new PPPLeaderboardInfo(Leaderboard.NoLeaderboard);
            Assert.AreEqual(leaderboardInfo.LeaderboardName, Leaderboard.NoLeaderboard.ToString());
            Assert.AreEqual(leaderboardInfo.PpSuffix, "pp");
            Assert.AreEqual(leaderboardInfo.LeaderboardFirstPageIndex, 1);
            Assert.IsTrue(leaderboardInfo.IsCountryRankEnabled);
            Assert.IsNotNull(leaderboardInfo.LeaderboardIcon);
            Assert.AreEqual(leaderboardInfo.LargePageSize, 10, "LargePageSize should be 10");
            Assert.AreEqual(leaderboardInfo.PlayerPerPages, 0, "PlayerPerPages should be 10");
            Assert.IsFalse(leaderboardInfo.HasGetAllScoresFunctionality, "HasGetAllScoresFunctionality should be false");
            Assert.IsTrue(leaderboardInfo.HasGetRecentScoresFunctionality, "HasGetRecentScoresFunctionality should be true");
            Assert.IsFalse(leaderboardInfo.HasPPToRankFunctionality, "HasPPToRankFunctionality should be false");
            Assert.AreEqual(leaderboardInfo.TaskDelayValue, 250, "TaskDelayValue should be 250");
            Assert.IsTrue(leaderboardInfo.HasOldDotRanking, "HasOldDotRanking should be true");
            Assert.AreEqual(leaderboardInfo.PageFetchLimit, 5, "LargePageSize should be 5");
        }

        [TestMethod]
        public void HitBloqConstructor()
        {
            PPPLeaderboardInfo leaderboardInfo = new PPPLeaderboardInfo(Leaderboard.HitBloq);
            Assert.AreEqual(leaderboardInfo.LeaderboardName, Leaderboard.HitBloq.ToString());
            Assert.AreEqual(leaderboardInfo.PpSuffix, "cr");
            Assert.AreEqual(leaderboardInfo.LeaderboardFirstPageIndex, 0);
            Assert.IsFalse(leaderboardInfo.IsCountryRankEnabled);
            Assert.IsNotNull(leaderboardInfo.LeaderboardIcon);
            Assert.AreEqual(leaderboardInfo.LargePageSize, 10, "LargePageSize should be 10");
            Assert.AreEqual(leaderboardInfo.PlayerPerPages, 10, "PlayerPerPages should be 10");
            Assert.IsTrue(leaderboardInfo.HasGetAllScoresFunctionality, "HasGetAllScoresFunctionality should be false");
            Assert.IsTrue(leaderboardInfo.HasGetRecentScoresFunctionality, "HasGetRecentScoresFunctionality should be true");
            Assert.IsTrue(leaderboardInfo.HasPPToRankFunctionality, "HasPPToRankFunctionality should be false");
            Assert.AreEqual(leaderboardInfo.TaskDelayValue, 250, "TaskDelayValue should be 250");
            Assert.IsTrue(leaderboardInfo.HasOldDotRanking, "HasOldDotRanking should be true");
            Assert.AreEqual(leaderboardInfo.PageFetchLimit, 5, "LargePageSize should be 5");
        }

        [TestMethod]
        public void AccSaberConstructor()
        {
            PPPLeaderboardInfo leaderboardInfo = new PPPLeaderboardInfo(Leaderboard.AccSaber);
            Assert.AreEqual(leaderboardInfo.LeaderboardName, Leaderboard.AccSaber.ToString());
            Assert.AreEqual(leaderboardInfo.PpSuffix, "ap");
            Assert.AreEqual(leaderboardInfo.LeaderboardFirstPageIndex, 0);
            Assert.IsFalse(leaderboardInfo.IsCountryRankEnabled);
            Assert.IsNotNull(leaderboardInfo.LeaderboardIcon);
            Assert.AreEqual(leaderboardInfo.LargePageSize, 10, "LargePageSize should be 10");
            Assert.AreEqual(leaderboardInfo.PlayerPerPages, 0, "PlayerPerPages should be 10");
            Assert.IsTrue(leaderboardInfo.HasGetAllScoresFunctionality, "HasGetAllScoresFunctionality should be false");
            Assert.IsFalse(leaderboardInfo.HasGetRecentScoresFunctionality, "HasGetRecentScoresFunctionality should be true");
            Assert.IsFalse(leaderboardInfo.HasPPToRankFunctionality, "HasPPToRankFunctionality should be false");
            Assert.AreEqual(leaderboardInfo.TaskDelayValue, 250, "TaskDelayValue should be 250");
            Assert.IsTrue(leaderboardInfo.HasOldDotRanking, "HasOldDotRanking should be true");
            Assert.AreEqual(leaderboardInfo.PageFetchLimit, 5, "LargePageSize should be 5");
        }
    }
}
