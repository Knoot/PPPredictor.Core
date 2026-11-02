using PPPredictor.Core.DataType.LeaderBoard;
using PPPredictor.Core.Interface;
using static PPPredictor.Core.DataType.LeaderBoard.ScoreSaberDataTypes;

namespace PPPredictor.Core.UnitTest.MockService
{
    internal class MockScoreSaberApi : IScoresaberAPI
    {
        private List<ScoreSaberPlayer> lsPlayer = new List<ScoreSaberPlayer>();
        public MockScoreSaberApi()
        {
            for (int i = 0; i < 3000; i++)
            {
                lsPlayer.Add(new ScoreSaberPlayer()
                {
                    country = "DE",
                    countryRank = i + 1,
                    rank = i + 1,
                    pp = 20000 - (i * 5),
                });
            }
        }

        public Task<ScoreSaberDataTypes.ScoreSaberPlayer> GetPlayer(long playerId)
        {
            return Task.FromResult(new ScoreSaberDataTypes.ScoreSaberPlayer()
            {
                country = "DE",
                countryRank = 100,
                rank = 2000,
                pp = 11000
            });
        }

        public Task<ScoreSaberDataTypes.ScoreSaberPlayerList> GetPlayers(double? page)
        {
            var v = new ScoreSaberPlayerList()
            {
                players = lsPlayer.Skip((int)(page.GetValueOrDefault() - 1) * 50).Take(50).ToList()
            };
            return Task.FromResult(v);
        }

        public Task<ScoreSaberDataTypes.ScoreSaberPlayerScoreList> GetPlayerScores(string playerId, int? limit, int? page)
        {
            throw new NotImplementedException();
        }

        public Task<ScoreSaberLeaderboardInfo> GetLeaderboardInfo(string hash, int difficulty, string gameMode)
            => throw new NotSupportedException();
    }
}
