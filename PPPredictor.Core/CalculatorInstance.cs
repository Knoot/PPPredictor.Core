using PPPredictor.Core.API;
using PPPredictor.Core.Calculator;
using PPPredictor.Core.DataType;
using PPPredictor.Core.DataType.LeaderBoard;
using PPPredictor.Core.DataType.MapPool;
using PPPredictor.Core.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static PPPredictor.Core.DataType.Enums;

namespace PPPredictor.Core
{
    public class CalculatorInstance : IDisposable
    {
        Dictionary<Leaderboard, PPCalculator> dctCalculator = new Dictionary<Leaderboard, PPCalculator> ();
        public event EventHandler<LoggingMessage> OnMessage;
        private Settings settings;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly CancellationToken lifetimeToken;
        private int disposed;
        private readonly object lifetimeGate = new object();
        private Task shutdown;
        public bool IsDisposed => Volatile.Read(ref disposed) != 0;

        public CalculatorInstance(Settings settings)
        {
            this.settings = settings;
            lifetimeToken = lifetime.Token;
            Logging.OnMessage += ForwardMessage;
        }

        private void ForwardMessage(object sender, LoggingMessage message)
        {
            if (!IsDisposed) OnMessage?.Invoke(this, message);
        }

        public void Dispose()
        {
            lock (lifetimeGate)
            {
                if (Interlocked.Exchange(ref disposed, 1) != 0) return;
                Logging.OnMessage -= ForwardMessage;
                OnMessage = null;
                lifetime.Cancel();
            }
        }

        public Task ShutdownAsync()
        {
            lock (lifetimeGate) return shutdown ?? (shutdown = ShutdownCoreAsync());
        }

        private async Task ShutdownCoreAsync()
        {
            Dispose();
            foreach (var calculator in dctCalculator.Values)
            {
                await calculator.Semaphore.WaitAsync().ConfigureAwait(false);
                calculator.Semaphore.Release();
            }
            foreach (var calculator in dctCalculator.Values) calculator.Dispose();
            lifetime.Dispose();
        }

        public DateTime LastSessionReset
        {
            get => settings.LastSessionReset;
        }

        public static async Task<CalculatorInstance> CreateAsync(Settings settings, Dictionary<string, LeaderboardData> dctLeaderboardData, Func<PPPBeatMapInfo, PPPBeatMapInfo> scoreSaberLookUpFunction, CancellationToken cancellationToken = default)
        {
            var instance = new CalculatorInstance(settings);
            try
            {
                using (cancellationToken.Register(instance.Dispose))
                {
                    await instance.InitializeOwnedAsync<ScoresaberAPI, BeatleaderAPI, HitbloqAPI, AccSaberApi>(settings, dctLeaderboardData, scoreSaberLookUpFunction);
                    return instance;
                }
            }
            catch { await instance.ShutdownAsync(); throw; }
        }

        internal static async Task<CalculatorInstance> CreateAsyncMock<SSAPI, BLAPI, HBAPI, ASAPI>(Settings settings, Dictionary<string, LeaderboardData> dctLeaderboardData, Func<PPPBeatMapInfo, PPPBeatMapInfo> scoreSaberLookUpFunction) where SSAPI : IScoresaberAPI, new() where BLAPI : IBeatLeaderAPI, new() where HBAPI : IHitBloqAPI, new() where ASAPI : IAccSaberAPI, new()
        {
            var instance = new CalculatorInstance(settings);
            try
            {
                await instance.InitializeOwnedAsync<SSAPI, BLAPI, HBAPI, ASAPI>(settings, dctLeaderboardData, scoreSaberLookUpFunction);
                return instance;
            }
            catch { await instance.ShutdownAsync(); throw; }
        }

        private async Task InitializeOwnedAsync<SSAPI, BLAPI, HBAPI, ASAPI>(Settings settings, Dictionary<string, LeaderboardData> data, Func<PPPBeatMapInfo, PPPBeatMapInfo> lookup)
            where SSAPI : IScoresaberAPI, new() where BLAPI : IBeatLeaderAPI, new() where HBAPI : IHitBloqAPI, new() where ASAPI : IAccSaberAPI, new()
        {
            var previous = NetworkUtil.RequestCancellation;
            NetworkUtil.RequestCancellation = lifetimeToken;
            try
            {
                lifetimeToken.ThrowIfCancellationRequested();
                await InitializeAsync<SSAPI, BLAPI, HBAPI, ASAPI>(settings, data, lookup);
                lifetimeToken.ThrowIfCancellationRequested();
            }
            finally { NetworkUtil.RequestCancellation = previous; }
        }

        private async Task InitializeAsync<SSAPI, BLAPI, HBAPI, ASAPI>(Settings settings, Dictionary<string, LeaderboardData> dctLeaderboardData, Func<PPPBeatMapInfo, PPPBeatMapInfo> scoreSaberLookUpFunction) where SSAPI : IScoresaberAPI, new() where BLAPI : IBeatLeaderAPI, new() where HBAPI : IHitBloqAPI, new() where ASAPI : IAccSaberAPI, new()
        {

            if (settings.IsScoreSaberEnabled)
            {
                if (scoreSaberLookUpFunction == null) throw new Exception("ScoreSaberLookUpFunction is missing");
                var v = new PPCalculatorScoreSaber<SSAPI>(dctLeaderboardData.TryGetValue(Leaderboard.ScoreSaber.ToString(), out var result) ? result.DctMapPool : null, settings, scoreSaberLookUpFunction);
                dctCalculator.Add(Leaderboard.ScoreSaber, v);
            }
            if(settings.IsBeatLeaderEnabled)
            {
                var v = new PPCalculatorBeatLeader<BLAPI>(dctLeaderboardData.TryGetValue(Leaderboard.BeatLeader.ToString(), out var result) ? result.DctMapPool : null, settings);
                dctCalculator.Add(Leaderboard.BeatLeader, v);
            }
            if (settings.IsHitbloqEnabled)
            {
                var v = new PPCalculatorHitBloq<HBAPI>(dctLeaderboardData.TryGetValue(Leaderboard.HitBloq.ToString(), out var result) ? result.DctMapPool : null, settings);
                dctCalculator.Add(Leaderboard.HitBloq, v);
            }
            if (settings.IsAccSaberEnabled)
            {
                var v = new PPCalculatorAccSaber<ASAPI>(dctLeaderboardData.TryGetValue(Leaderboard.AccSaber.ToString(), out var result) ? result.DctMapPool : null, settings);
                dctCalculator.Add(Leaderboard.AccSaber, v);
            }
            if(dctCalculator.Count == 0)
            {
                var v = new PPCalculatorNoLeaderboard(null, settings);
                dctCalculator.Add(Leaderboard.NoLeaderboard, v);
            }

            var updateAvailableMapPoolsTask = dctCalculator.Values.Select(item => item.UpdateAvailableMapPools());
            await Task.WhenAll(updateAvailableMapPoolsTask);
        }

        public void AssignSettings(Settings settings)
        {
            this.settings = settings;
            foreach (var item in dctCalculator.Values)
            {
                item.Settings = settings;
            }
        }

        internal PPCalculator GetCalculator(Leaderboard leaderboard)
        {
            if(dctCalculator.TryGetValue(leaderboard, out PPCalculator calculator)){
                return calculator;
            }
            else
            {
                throw new Exception($"PPCalculator not found for {leaderboard}");
            }
        }

        public double CalculatePPatPercentage(Leaderboard leaderBoard, string mapPoolId, PPPBeatMapInfo _currentBeatMapInfo, double percentage, bool failed, bool paused)
        {
            (PPCalculator calculator, PPPMapPool mapPool) = GetCalculatorAndMapPool(leaderBoard, mapPoolId);
            return calculator.CalculatePPatPercentage(_currentBeatMapInfo, mapPool, percentage, failed, paused);
        }

        public PPPBeatMapInfo ApplyModifiersToBeatmapInfo(Leaderboard leaderBoard, string mapPoolId, PPPBeatMapInfo beatMapInfo, DataType.BeatSaberEncapsulation.GameplayModifiers gameplayModifiers, bool levelFailed = false, bool levelPaused = false)
        {
            (PPCalculator calculator, PPPMapPool mapPool) = GetCalculatorAndMapPool(leaderBoard, mapPoolId);
            return calculator.ApplyModifiersToBeatmapInfo(beatMapInfo, mapPool, gameplayModifiers, levelFailed, levelPaused);
        }

        public double CalculateMaxPP(Leaderboard leaderBoard, string mapPoolId, PPPBeatMapInfo currentBeatMapInfo)
        {
            try
            {
                (PPCalculator calculator, PPPMapPool mapPool) = GetCalculatorAndMapPool(leaderBoard, mapPoolId);
                return calculator.CalculateMaxPP(currentBeatMapInfo, mapPool);
            }
            catch (Exception ex)
            {

                throw new Exception($"Error in CalculateMaxPP {ex.Message}");
            }
        }

        public async Task<RankGainResult> GetPlayerRankGain(Leaderboard leaderBoard, string mapPoolId, double pp)
        {
            (PPCalculator calculator, PPPMapPool mapPool) = GetCalculatorAndMapPool(leaderBoard, mapPoolId);
            var i = await SemaphoreFunction(calculator.Semaphore,
                () => Task.Run(() => calculator.GetPlayerRankGain(pp, mapPool))
            );
            return i;
        }

        public PPGainResult GetPlayerScorePPGain(Leaderboard leaderBoard, string mapPoolId, string mapSearchString, double pp)
        {
            (PPCalculator calculator, PPPMapPool mapPool) = GetCalculatorAndMapPool(leaderBoard, mapPoolId);
            lock (mapPool.DataLock) return calculator.GetPlayerScorePPGain(mapSearchString, pp, mapPool);
        }

        public bool HasScoreDataForPPGain(Leaderboard leaderBoard, string mapPoolId)
        {
            (PPCalculator calculator, PPPMapPool mapPool) = GetCalculatorAndMapPool(leaderBoard, mapPoolId);
            lock (mapPool.DataLock) return calculator.HasScoreDataForPPGain(mapPool);
        }

        public PPPMapPoolShort FindPoolWithPlayListId(Leaderboard leaderBoard, string playListId)
        {
            PPCalculator calculator = GetCalculator(leaderBoard);
            return calculator.FindPoolWithPlayListId(playListId);
        }
        public PPPMapPoolShort FindPoolWithSyncURL(Leaderboard leaderBoard, string syncUrl)
        {
            PPCalculator calculator = GetCalculator(leaderBoard);
            return calculator.FindPoolWithSyncURL(syncUrl);
        }

        public PPPMapPoolShort FindMapPoolForBeatMapInfo(Leaderboard leaderBoard, PPPBeatMapInfo beatMapInfo)
        {
            PPCalculator calculator = GetCalculator(leaderBoard);
            return calculator.FindMapPoolForBeatMapInfo(beatMapInfo);
        }

        public async Task<PPPMapPoolShort> FindMapPoolForBeatMapInfoAsync(Leaderboard leaderBoard, PPPBeatMapInfo beatMapInfo)
        {
            PPCalculator calculator = GetCalculator(leaderBoard);
            return await SemaphoreFunction(calculator.Semaphore,
                () => Task.Run(() => calculator.FindMapPoolForBeatMapInfoAsync(beatMapInfo)));
        }

        public List<PPPMapPoolShort> GetMapPools(Leaderboard leaderBoard)
        {
            PPCalculator calculator = GetCalculator(leaderBoard);
            return calculator.GetMapPools();
        }
        


        public double? GetPersonalBest(Leaderboard leaderBoard, string mapPoolId, string mapSearchString)
        {
            (PPCalculator calculator, PPPMapPool mapPool) = GetCalculatorAndMapPool(leaderBoard, mapPoolId);
            lock (mapPool.DataLock) return calculator.GetPersonalBest(mapSearchString, mapPool);
        }

        
        public async Task<(PPPPlayer, PPPPlayer)> UpdatePlayer(Leaderboard leaderBoard, string mapPoolId, bool doResetSession)
        {
            (PPCalculator calculator, PPPMapPool mapPool) = GetCalculatorAndMapPool(leaderBoard, mapPoolId);
            var i = await SemaphoreFunction(calculator.Semaphore,
                () => Task.Run(() => calculator.UpdatePlayer(mapPool, doResetSession))
            );
            return i;
        }

        public bool IsScoreSetOnCurrentMapPool(Leaderboard leaderBoard, string mapPoolId, PPPScoreSetData data)
        {
            (PPCalculator calculator, PPPMapPool mapPool) = GetCalculatorAndMapPool(leaderBoard, mapPoolId);
            lock (mapPool.DataLock) return calculator.IsScoreSetOnCurrentMapPool(mapPool, data);
        }

        public async Task<PPPBeatMapInfo> GetBeatMapInfoAsync(Leaderboard leaderBoard, string mapPoolId, PPPBeatMapInfo beatMapInfo)
        {
            (PPCalculator calculator, PPPMapPool mapPool) = GetCalculatorAndMapPool(leaderBoard, mapPoolId);
            var i = await SemaphoreFunction(calculator.Semaphore,
                () => Task.Run(() => calculator.GetBeatMapInfoAsync(beatMapInfo, mapPool))
            );
            return i;
        }

        public async Task UpdateMapPoolDetails(Leaderboard leaderBoard, string mapPoolId)
        {
            (PPCalculator calculator, PPPMapPool mapPool) = GetCalculatorAndMapPool(leaderBoard, mapPoolId);
            await SemaphoreFunction(calculator.Semaphore,
                () => Task.Run(() => calculator.UpdateMapPoolDetails(mapPool))
            );
        }

        public async Task<bool> GetPlayerScores(Leaderboard leaderBoard, string mapPoolId, int pageSize, int largePageSize, bool fetchOnePage = false, Action<int, int> progressChanged = null, PPPScoreSetData expectedScore = null, bool forceFullRefresh = false)
        {
            (PPCalculator calculator, PPPMapPool mapPool) = GetCalculatorAndMapPool(leaderBoard, mapPoolId);
            return await SemaphoreFunction(calculator.Semaphore,
                () => Task.Run(() => calculator.GetPlayerScores(mapPool, pageSize, largePageSize, fetchOnePage, progressChanged, expectedScore, forceFullRefresh))
            );
        }

        public bool HasDataRefreshFailed(Leaderboard leaderBoard, string mapPoolId)
        {
            var (_, mapPool) = GetCalculatorAndMapPool(leaderBoard, mapPoolId);
            lock (mapPool.DataLock) return mapPool.ScoreRefreshFailed || mapPool.ProfileRefreshFailed;
        }

        public bool RequiresScoreRefresh(Leaderboard leaderBoard, string mapPoolId)
        {
            var (_, pool) = GetCalculatorAndMapPool(leaderBoard, mapPoolId);
            lock (pool.DataLock) return pool.ScoresRequireRefresh;
        }

        public Dictionary<string, LeaderboardData> GetSaveData()
        {
            Dictionary<string, LeaderboardData> dctLeaderboardData = new Dictionary<string, LeaderboardData>();
            foreach (var calculator in dctCalculator.Values)
            {
                LeaderboardData data = new LeaderboardData()
                {
                    DctMapPool = calculator._dctMapPool.ToDictionary(x => x.Key, x => CopyMapPool(x.Value))
                };
            dctLeaderboardData.Add(calculator._leaderboardInfo.LeaderboardName, data);
            }
            return dctLeaderboardData;
        }

        private static PPPMapPool CopyMapPool(PPPMapPool pool)
        {
            lock (pool.DataLock)
                return Newtonsoft.Json.JsonConvert.DeserializeObject<PPPMapPool>(Newtonsoft.Json.JsonConvert.SerializeObject(pool,
                    new Newtonsoft.Json.JsonSerializerSettings { NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore }));
        }

        private (PPCalculator, PPPMapPool) GetCalculatorAndMapPool(Leaderboard leaderBoard, string mapPoolId)
        {
            var calculator = GetCalculator(leaderBoard);
            var mapPool = calculator.GetMapPoolById(mapPoolId);
            return (calculator, mapPool);
        }

        internal string GetStarDisplayForCalculator(Leaderboard leaderboardName, string mapPoolId, PPPBeatMapInfo beatMapInfo)
        {
            (PPCalculator calculator, PPPMapPool mapPool) = GetCalculatorAndMapPool(leaderboardName, mapPoolId);
            return calculator.GetStarDisplay(beatMapInfo);
        }

        private async Task<T> SemaphoreFunction<T>(SemaphoreSlim sem, Func<Task<T>> function)
        {
            lifetimeToken.ThrowIfCancellationRequested();
            await sem.WaitAsync(lifetimeToken);
            var previous = NetworkUtil.RequestCancellation;
            NetworkUtil.RequestCancellation = lifetimeToken;
            try
            {
                lifetimeToken.ThrowIfCancellationRequested();
                T result = await function();
                lifetimeToken.ThrowIfCancellationRequested();
                return result;
            }
            finally
            {
                NetworkUtil.RequestCancellation = previous;
                sem.Release();
            }
        }

        private Task SemaphoreFunction(SemaphoreSlim sem, Func<Task> function)
        {
            return SemaphoreFunction(sem, async () => { await function(); return true; });
        }
    }
}
