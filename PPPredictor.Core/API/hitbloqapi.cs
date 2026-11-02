
using PPPredictor.Core.Interface;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using static PPPredictor.Core.DataType.LeaderBoard.HitBloqDataTypes;

namespace PPPredictor.Core.API
{
    [ExcludeFromCodeCoverage]
    class HitbloqAPI : IHitBloqAPI, IDisposable
    {
        private static readonly string baseUrl = "https://hitbloq.com";
        private readonly HttpClient client;
        public void Dispose() { client.Dispose(); }

        public HitbloqAPI()
        {
            client = new HttpClient();
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Add("User-Agent", "PPPredictor");
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            client.BaseAddress = new Uri(baseUrl);
        }

        public async Task<HitBloqUserId> GetHitBloqUserIdByUserId(string id)
        {
            return await NetworkUtil.GetDataAsync<HitBloqUserId>(client, DataType.Enums.Leaderboard.HitBloq, "GetHitBloqUserIdByUserId", $"api/tools/ss_to_hitbloq/{id}");
        }

        public async Task<List<HitBloqMapPool>> GetHitBloqMapPools()
        {
            return await NetworkUtil.GetDataAsync<List<HitBloqMapPool>>(client, DataType.Enums.Leaderboard.HitBloq, "GetHitBloqMapPools", $"api/map_pools_detailed");
        }

        public async Task<HitBloqMapPoolDetails> GetHitBloqMapPoolDetails(string poolIdent, int page)
        {
            return await NetworkUtil.GetDataAsync<HitBloqMapPoolDetails>(client, DataType.Enums.Leaderboard.HitBloq, "GetHitBloqMapPoolDetails", $"api/ranked_list/{poolIdent}/{page}");
        }

        public async Task<HitBloqUser> GetHitBloqUserByPool(long userId, string poolIdent)
        {
            return await NetworkUtil.GetDataAsync<HitBloqUser>(client, DataType.Enums.Leaderboard.HitBloq, "GetHitBloqUserByPool", $"api/player_rank/{poolIdent}/{userId}");
        }

        public async Task<List<HitBloqScores>> GetRecentScores(string userId, string poolId, int page)
        {
            return await NetworkUtil.GetDataAsync< List<HitBloqScores>>(client, DataType.Enums.Leaderboard.HitBloq, "GetRecentScores", $"api/user/{userId}/scores?page={page}&pool={poolId}&sort=newest");
        }

        public async Task<List<HitBloqScores>> GetAllScores(string userId, string poolId)
        {
            return await NetworkUtil.GetDataAsync< List<HitBloqScores>>(client, DataType.Enums.Leaderboard.HitBloq, "GetAllScores", $"api/user/{userId}/all_scores?pool={poolId}");
        }

        public async Task<HitBloqLadder> GetPlayerListForMapPool(double page, string mapPoolId)
        {
            return await NetworkUtil.GetDataAsync<HitBloqLadder>(client, DataType.Enums.Leaderboard.HitBloq, "GetPlayerListForMapPool", $"api/ladder/{mapPoolId}/players/{page}");
        }

        public async Task<HitBloqRankFromCr> GetPlayerRankByCr(string mapPoolId, double cr)
        {
            return await NetworkUtil.GetDataAsync<HitBloqRankFromCr>(client, DataType.Enums.Leaderboard.HitBloq, "GetPlayerRankByCr", $"api/ladder/{mapPoolId}/cr_to_rank/{cr.ToString(new CultureInfo("en-US"))}");
        }

        public async Task<HitBloqLeaderboardInfo> GetLeaderBoardInfo(string searchString)
        {
            return await NetworkUtil.GetDataAsync<HitBloqLeaderboardInfo>(client, DataType.Enums.Leaderboard.HitBloq, "GetLeaderBoardInfo", $"api/leaderboard/{searchString}/info");
        }
    }
}
