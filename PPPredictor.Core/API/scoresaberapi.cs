using PPPredictor.Core.Interface;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using static PPPredictor.Core.DataType.LeaderBoard.ScoreSaberDataTypes;

namespace PPPredictor.Core.API
{
    [ExcludeFromCodeCoverage]
    internal class ScoresaberAPI : IScoresaberAPI
    {
        private static readonly string baseUrl = "https://scoresaber.com/api/";
        private readonly HttpClient client;

        public ScoresaberAPI()
        {
            client = new HttpClient();
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Add("User-Agent", "PPPredictor");
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            client.BaseAddress = new Uri(baseUrl);
        }

        public async Task<ScoreSaberPlayerList> GetPlayers(double? page)
        {
            return await NetworkUtil.GetDataAsync<ScoreSaberPlayerList>(client, DataType.Enums.Leaderboard.ScoreSaber, "GetPlayers", $"players?&page={page}&withMetadata=true");
        }

        public async Task<ScoreSaberPlayer> GetPlayer(long playerId)
        {
            return await NetworkUtil.GetDataAsync<ScoreSaberPlayer>(client, DataType.Enums.Leaderboard.ScoreSaber, "GetPlayer", $"player/{playerId}/basic");
        }

        public async Task<ScoreSaberPlayerScoreList> GetPlayerScores(string playerId, int? limit, int? page)
        {
            return await NetworkUtil.GetDataAsync<ScoreSaberPlayerScoreList>(client, DataType.Enums.Leaderboard.ScoreSaber, "GetPlayerScores", $"player/{playerId}/scores?limit={limit}&sort=recent&page={page}&withMetadata=true");
        }

        public Task<ScoreSaberLeaderboardInfo> GetLeaderboardInfo(string hash, int difficulty, string gameMode)
        {
            return NetworkUtil.GetDataAsync<ScoreSaberLeaderboardInfo>(client, DataType.Enums.Leaderboard.ScoreSaber,
                "GetLeaderboardInfo", $"leaderboard/by-hash/{Uri.EscapeDataString(hash)}/info?difficulty={difficulty}&gameMode={Uri.EscapeDataString(gameMode)}", allowNotFound: true);
        }
    }
}
