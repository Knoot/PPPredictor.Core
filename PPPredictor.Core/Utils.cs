using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
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
        // Calculator operations carry their lifetime through existing API adapters without sharing a global token.
        private static readonly AsyncLocal<CancellationToken> requestCancellation = new AsyncLocal<CancellationToken>();
        internal static CancellationToken RequestCancellation { get => requestCancellation.Value; set => requestCancellation.Value = value; }
        internal static async System.Threading.Tasks.Task<T> GetDataAsync<T>(HttpClient client, Leaderboard leaderboard, string callingMethod, string requestUrl, bool allowNotFound = false) where T : new()
        {
            var cancellation = RequestCancellation;
            for (int attempt = 0; ; attempt++)
            {
                cancellation.ThrowIfCancellationRequested();
                bool receivedResponse = false;
                try
                {
                    using (HttpResponseMessage response = await client.GetAsync(requestUrl, cancellation))
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
                                await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, cancellation);
                                continue;
                            }
                        }
                        response.EnsureSuccessStatusCode();
                        string result = await response.Content.ReadAsStringAsync();
                        cancellation.ThrowIfCancellationRequested();
                        T data = JsonConvert.DeserializeObject<T>(result);
                        if (data == null) throw new JsonSerializationException("API returned an empty or null response.");
                        return data;
                    }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
                catch (Exception ex) when (!receivedResponse && attempt < 2 && (ex is HttpRequestException || ex is TaskCanceledException))
                {
                    await Task.Delay(250 * (attempt + 1), cancellation);
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
