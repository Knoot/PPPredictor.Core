using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
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
        internal static async System.Threading.Tasks.Task<T> GetDataAsync<T>(HttpClient client, Leaderboard leaderboard, string callingMethod, string requestUrl) where T : new()
        {
            try
            {
                HttpResponseMessage response = await client.GetAsync(requestUrl);
                Logging.DebugNetworkPrint($"{leaderboard}Network: {response.RequestMessage.RequestUri.ToString()}", leaderboard);
                if (response.IsSuccessStatusCode)
                {
                    string result = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<T>(result);
                }
                else
                {
                    Logging.ErrorPrint($"Error in {leaderboard}Network {callingMethod}: {response?.StatusCode} {response?.ReasonPhrase} for {requestUrl}");
                }
            }
            catch (Exception ex)
            {
                Logging.ErrorPrint($"Error in {leaderboard}Network {callingMethod}: {ex.Message} for {requestUrl}");
            }
            return new T();
        }
    }
}
