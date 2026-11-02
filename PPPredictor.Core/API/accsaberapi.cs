using PPPredictor.Core.Interface;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using static PPPredictor.Core.DataType.LeaderBoard.AccSaberDataTypes;

namespace PPPredictor.Core.API
{
    [ExcludeFromCodeCoverage]
    internal class AccSaberApi : IAccSaberAPI
    {
        private static readonly string baseUrl = "https://api.accsaber.com/v1/";
        private readonly HttpClient client;
        private readonly TimedAsyncCache<List<AccSaberMapPool>> categories;

        public AccSaberApi() : this(new HttpClient())
        {
        }

        internal AccSaberApi(HttpClient client, Func<DateTime> utcNow = null)
        {
            this.client = client;
            categories = new TimedAsyncCache<List<AccSaberMapPool>>(TimeSpan.FromHours(1), utcNow);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Add("User-Agent", "PPPredictor");
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            client.BaseAddress = new Uri(baseUrl);
        }

        public async Task<List<AccSaberScores>> GetAllScores(string userId)
        {
            List<AccSaberScores> scores = new List<AccSaberScores>();
            bool hasMoreData = true;
            int pageIndex = 0;
            while (hasMoreData)
            {
                AccSaberScorePage page = await NetworkUtil.GetDataAsync<AccSaberScorePage>(client, DataType.Enums.Leaderboard.AccSaber, "GetAllScores", $"users/{userId}/scores?page={pageIndex}&size=100");
                if (page.content == null || (page.content.Count == 0 && !page.last))
                    throw new InvalidOperationException("Invalid or incomplete AccSaber scores page.");
                scores.AddRange(page.content ?? new List<AccSaberScores>());
                hasMoreData = page.content != null && page.content.Count > 0 && !page.last;
                pageIndex++;
            }

            await EnrichScoreCategories(scores);
            return scores;
        }

        public async Task<List<AccSaberScores>> GetAllScoresByPool(string userId, string poolId)
        {
            List<AccSaberScores> scores = await GetAllScores(userId);
            string categoryId = await GetCategoryId(poolId);
            if (string.IsNullOrEmpty(categoryId))
            {
                throw new InvalidOperationException($"AccSaber category not found: {poolId}");
            }

            return scores.Where(x => x.categoryId == categoryId).ToList();
        }

        public async Task<AccSaberPlayer> GetAccSaberUserByPool(long userId, string poolIdent)
        {
            AccSaberStatisticsCollection statistics = await NetworkUtil.GetDataAsync<AccSaberStatisticsCollection>(client, DataType.Enums.Leaderboard.AccSaber, "GetAccSaberUserByPool", $"users/{userId}/statistics/all");
            string categoryId = await GetCategoryId(poolIdent);
            if (statistics.categories == null || string.IsNullOrEmpty(categoryId))
                throw new InvalidOperationException("Invalid AccSaber statistics or unknown category.");
            AccSaberStatistics categoryStatistics = statistics.categories?.FirstOrDefault(x => x.categoryId == categoryId);
            if (categoryStatistics == null)
            {
                return new AccSaberPlayer();
            }

            return new AccSaberPlayer
            {
                rank = categoryStatistics.ranking,
                ap = categoryStatistics.ap
            };
        }

        public async Task<List<AccSaberMapPool>> GetAccSaberMapPools()
        {
            return (await GetCategories())
                .Where(x => x.categoryName != "overall")
                .ToList();
        }

        public Task<List<AccSaberRankedMap>> GetAllRankedMaps()
        {
            return Task.FromResult(new List<AccSaberRankedMap>());
        }

        public Task<List<AccSaberRankedMap>> GetRankedMaps(string mapPool)
        {
            return Task.FromResult(new List<AccSaberRankedMap>());
        }

        public async Task<AccSaberMap> GetMapByHash(string hash)
        {
            AccSaberMap map = await NetworkUtil.GetDataAsync<AccSaberMap>(client, DataType.Enums.Leaderboard.AccSaber, "GetMapByHash", $"maps/hash/{hash}", allowNotFound: true);
            if (map == null) return null;
            if (map.difficulties == null) throw new InvalidOperationException("AccSaber returned an invalid map response.");
            map.difficulties = map.difficulties.Where(x => x.status == null || string.Equals(x.status, "RANKED", StringComparison.OrdinalIgnoreCase)).ToList();
            if (map.difficulties.Count == 0) return null;

            Dictionary<string, string> displayNamesById = (await GetCategories()).ToDictionary(x => x.id, x => x.categoryDisplayName);
            foreach (AccSaberMapDifficulty difficulty in map.difficulties)
            {
                if (difficulty.categoryId == null || !displayNamesById.TryGetValue(difficulty.categoryId, out string displayName))
                    throw new InvalidOperationException("AccSaber map references an unknown category.");
                difficulty.categoryId = displayName;
            }

            return map;
        }

        public Task<List<AccSaberPlayer>> GetPlayerListForMapPool(double page, string mapPoolId)
        {
            return Task.FromResult(new List<AccSaberPlayer>());
        }

        private async Task<List<AccSaberMapPool>> GetCategories()
        {
            var result = await categories.Get("categories", async () =>
            {
                List<AccSaberMapPool> apiCategories = await NetworkUtil.GetDataAsync<List<AccSaberMapPool>>(client, DataType.Enums.Leaderboard.AccSaber, "GetAccSaberMapPools", "categories");
                if (apiCategories.Count == 0 || apiCategories.Any(x => string.IsNullOrEmpty(x.id) || string.IsNullOrEmpty(x.code)))
                    throw new InvalidOperationException("AccSaber returned an empty or invalid category list.");
                var normalized = apiCategories
                .Select(x => new AccSaberMapPool
                {
                    id = x.id,
                    code = x.code,
                    name = x.name,
                    categoryName = NormalizeCategoryToPoolId(x.code),
                    categoryDisplayName = x.name
                })
                .Where(x => !string.IsNullOrEmpty(x.categoryName)).ToList();
                if (normalized.Count == 0) throw new InvalidOperationException("AccSaber returned no recognized categories.");
                return normalized;
            });
            if (result.Value == null) throw result.Error ?? new InvalidOperationException("AccSaber categories are unavailable.");
            return result.Value;
        }

        private async Task<string> GetCategoryId(string poolId)
        {
            AccSaberMapPool category = (await GetCategories()).FirstOrDefault(x => x.categoryName == NormalizeCategoryToPoolId(poolId));
            return category?.id;
        }

        private async Task EnrichScoreCategories(List<AccSaberScores> scores)
        {
            Dictionary<string, string> displayNamesById = (await GetCategories()).ToDictionary(x => x.id, x => x.categoryDisplayName);
            foreach (AccSaberScores score in scores)
            {
                if (score.categoryId != null && displayNamesById.TryGetValue(score.categoryId, out string displayName))
                {
                    score.categoryDisplayName = displayName;
                }
            }
        }

        private static string NormalizeCategoryToPoolId(string category)
        {
            string value = (category ?? string.Empty).ToLowerInvariant();
            if (value.Contains("standard")) return "standard";
            if (value.Contains("tech")) return "tech";
            if (value.Contains("true")) return "true";
            if (value.Contains("overall")) return "overall";
            return string.Empty;
        }
    }
}
