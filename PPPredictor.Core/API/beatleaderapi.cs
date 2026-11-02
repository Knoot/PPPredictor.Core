using PPPredictor.Core.Interface;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using static PPPredictor.Core.DataType.LeaderBoard.BeatLeaderDataTypes;

namespace PPPredictor.Core.API
{
    [ExcludeFromCodeCoverage]
    class BeatleaderAPI : IBeatLeaderAPI, IDisposable
    {
        private static readonly string baseUrl = "https://api.beatleader.com";
        private readonly HttpClient client;
        public void Dispose() { client.Dispose(); }

        public BeatleaderAPI()
        {
            client = new HttpClient();
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Add("User-Agent", "PPPredictor");
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            client.BaseAddress = new Uri(baseUrl);
        }

        public async Task<BeatLeaderEventList> GetEvents()
        {
            return await NetworkUtil.GetDataAsync<BeatLeaderEventList>(client, DataType.Enums.Leaderboard.BeatLeader, "GetEvents", $"/events?count=10000&sortBy=date&order=desc");
        }

        public async Task<BeatLeaderSong> GetSongByHash(string hash)
        {
            return await NetworkUtil.GetDataAsync<BeatLeaderSong>(client, DataType.Enums.Leaderboard.BeatLeader, "GetSongByHash", $"/map/hash/{hash}", allowNotFound: true);
        }

        public async Task<BeatLeaderPlayer> GetPlayer(long userId, long leaderboardContextId)
        {
            return await NetworkUtil.GetDataAsync<BeatLeaderPlayer>(client, DataType.Enums.Leaderboard.BeatLeader, "GetPlayer", $"/player/{userId}?stats=true&leaderboardContext={leaderboardContextId}");
        }

        public async Task<BeatLeaderPlayerList> GetPlayerForEvent(string eventId, string userName)
        {
            return await NetworkUtil.GetDataAsync<BeatLeaderPlayerList>(client, DataType.Enums.Leaderboard.BeatLeader, "GetPlayerForEvent", $"/event/{eventId}/players?search={Uri.EscapeDataString(userName)}");
        }

        public async Task<BeatLeaderPlayerScoreList> GetPlayerScores(string userId, string sortBy, string order, int page, int count, long leaderboardContextId, long? eventId = null)
        {
            string requestUrl = $"/player/{userId}/scores?sortBy={sortBy}&order={order}&page={page}&count={count}&leaderboardContext={leaderboardContextId}";
            if (eventId.GetValueOrDefault() > 0)
            {
                requestUrl = $"/player/{userId}/scores?sortBy={sortBy}&order={order}&page={page}&count={count}&eventId={eventId}";
            }
            return await NetworkUtil.GetDataAsync<BeatLeaderPlayerScoreList>(client, DataType.Enums.Leaderboard.BeatLeader, "GetPlayerScores", requestUrl);
        }

        public async Task<BeatLeaderPlayerList> GetPlayersInLeaderboard(string sortBy, int page, int? count, string order, long leaderboardContextId)
        {
            return await NetworkUtil.GetDataAsync<BeatLeaderPlayerList>(client, DataType.Enums.Leaderboard.BeatLeader, "GetPlayersInLeaderboard", $"players?sortBy={sortBy}&page={page}&count={count}&order={order}&mapsType=ranked&friends=false&leaderboardContext={leaderboardContextId}");
        }

        public async Task<BeatLeaderPlayerList> GetPlayersInEventLeaderboard(string eventId, string sortBy, int page, int? count, string order)
        {
            return await NetworkUtil.GetDataAsync<BeatLeaderPlayerList>(client, DataType.Enums.Leaderboard.BeatLeader, "GetPlayersInEventLeaderboard", $"event/{eventId}/players?sortBy={sortBy}&page={page}&count={count}&order={order}");
        }

        public async Task<BeatLeaderPlayListSongList> GetSongsInPlaylistById(long playListId)
        {
            return await NetworkUtil.GetDataAsync<BeatLeaderPlayListSongList>(client, DataType.Enums.Leaderboard.BeatLeader, "GetSongsInPlaylistById", $"maps?playlistIds={playListId}&count=250");
        }
    }
}
