using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using static PPPredictor.Core.DataType.Enums;

namespace PPPredictor.Core
{
    public class ParsingUtil
    {
        static readonly Dictionary<string, int> dctDifficultyNameToInt = new Dictionary<string, int>{
            { "EXPERTPLUS", 9 },
            { "EXPERT", 7 },
            { "HARD", 5 },
            { "NORMAL", 3 },
            { "EASY", 1 }
        };

        public static int ParseDifficultyNameToInt(string difficulty)
        {
            try
            {
                return dctDifficultyNameToInt[(difficulty ?? string.Empty).Replace("_", string.Empty).ToUpper()];
            }
            catch (Exception ex)
            {
                Logging.ErrorPrint($"Error in ParseDifficultyNameToInt could not parse {difficulty}, {ex.Message}");
            }
            return -1;
        }
    }

    public class NetworkUtil
    {
        internal static async System.Threading.Tasks.Task<T> GetDataAsync<T>(HttpClient client, Leaderboard leaderboard, string callingMethod, string requestUrl, bool allowNotFound = false) where T : new()
        {
            for (int attempt = 0; ; attempt++)
            {
                bool receivedResponse = false;
                try
                {
                    using (HttpResponseMessage response = await client.GetAsync(requestUrl))
                    {
                        receivedResponse = true;
                        if (allowNotFound && response.StatusCode == System.Net.HttpStatusCode.NotFound) return default(T);
                        Logging.DebugNetworkPrint($"{leaderboard}Network: {response.RequestMessage?.RequestUri}", leaderboard);
                        int status = (int)response.StatusCode;
                        if (attempt < 2 && (status == 408 || status == 429 || status >= 500))
                        {
                            TimeSpan delay = response.Headers.RetryAfter?.Delta
                                ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow)
                                ?? TimeSpan.FromMilliseconds(250 * (attempt + 1));
                            // Do not retry earlier than Retry-After or block the calculator for a long rate limit.
                            if (delay <= TimeSpan.FromSeconds(5))
                            {
                                await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.Zero);
                                continue;
                            }
                        }
                        response.EnsureSuccessStatusCode();
                        string result = await response.Content.ReadAsStringAsync();
                        T data = JsonConvert.DeserializeObject<T>(result);
                        if (data == null) throw new JsonSerializationException("API returned an empty or null response.");
                        return data;
                    }
                }
                catch (Exception ex) when (!receivedResponse && attempt < 2 && (ex is HttpRequestException || ex is TaskCanceledException))
                {
                    await Task.Delay(250 * (attempt + 1));
                }
                catch (Exception ex)
                {
                    Logging.ErrorPrint($"Error in {leaderboard}Network {callingMethod}: {ex.Message} for {requestUrl}");
                    throw;
                }
            }
        }
    }
}
