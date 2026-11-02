using System;
using System.Threading;
using System.Threading.Tasks;

namespace PPPredictor.Core
{
    internal sealed class ReconnectLoop : IDisposable
    {
        private readonly object gate = new object();
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly CancellationToken token;
        private readonly Func<CancellationToken, Action, Task> runConnection;
        private readonly Func<TimeSpan, CancellationToken, Task> delay;
        private readonly Action<Exception> reportError;
        private readonly Func<DateTime> utcNow;
        private Task completion = Task.CompletedTask;
        private bool started, stopped;

        internal event Action<bool> Connected;
        internal Task Completion { get { lock (gate) return completion; } }

        internal ReconnectLoop(Func<CancellationToken, Action, Task> runConnection, Action<Exception> reportError,
            Func<TimeSpan, CancellationToken, Task> delay = null, Func<DateTime> utcNow = null)
        {
            token = cancellation.Token;
            this.runConnection = runConnection;
            this.reportError = reportError;
            this.delay = delay ?? ((duration, token) => Task.Delay(duration, token));
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        internal void Start()
        {
            lock (gate)
            {
                if (started || stopped) return;
                started = true;
                completion = Task.Run(Run);
            }
        }

        private async Task Run()
        {
            bool reconnect = false;
            int failures = 0;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    bool opened = false;
                    DateTime openedAt = DateTime.MinValue;
                    bool active = true;
                    bool isReconnect = reconnect;
                    var attemptGate = new object();
                    try
                    {
                        // The attempt must release its transport before returning or throwing.
                        await runConnection(token, () =>
                        {
                            lock (attemptGate)
                            {
                                if (!active || token.IsCancellationRequested || opened) return;
                                opened = true;
                                openedAt = utcNow();
                                try { Connected?.Invoke(isReconnect); }
                                catch (Exception ex) { Report(ex); }
                            }
                        }).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                    catch (Exception ex) { Report(ex); }
                    finally { lock (attemptGate) active = false; }
                    if (token.IsCancellationRequested) break;
                    failures = opened && utcNow() - openedAt >= TimeSpan.FromSeconds(30) ? 0 : Math.Min(failures, 5);
                    var pause = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, failures++)));
                    reconnect = true;
                    await delay(pause, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }

        private void Report(Exception ex)
        {
            try { reportError?.Invoke(ex); }
            catch { /* Logging must not terminate recovery. */ }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (stopped) return;
                stopped = true;
                cancellation.Cancel();
                _ = completion.ContinueWith(_ => cancellation.Dispose(), TaskScheduler.Default);
            }
        }
    }
}
