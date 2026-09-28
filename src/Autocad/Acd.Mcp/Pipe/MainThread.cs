using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Acd.Mcp.Pipe
{
    // Runs work from a pipe (thread-pool) thread on AutoCAD's main thread and
    // waits for the result with a time limit. AutoCAD-free.
    //
    // Post, not Send: Send blocks the pipe thread for as long as the main
    // thread is busy (a running command, a modal dialog), with no limit.
    //
    // Work that has not started when the wait ends (timeout or cancel) never
    // runs: the caller already got a failure, so a late change would surprise
    // it. Work that has started is waited for, because it cannot be stopped.
    internal static class MainThread
    {
        private const int Pending = 0, Started = 1, Abandoned = 2;

        public static async Task<T> RunAsync<T>(
            SynchronizationContext main, Func<T> work, TimeSpan timeout, CancellationToken ct)
        {
            int state = Pending;
            var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            main.Post(_ =>
            {
                if (Interlocked.CompareExchange(ref state, Started, Pending) != Pending) return;
                try { done.SetResult(work()); }
                catch (Exception ex) { done.SetException(ex); }
            }, null);

            try
            {
                return await done.Task.WaitAsync(timeout, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
            {
                if (Interlocked.CompareExchange(ref state, Abandoned, Pending) != Pending)
                    return await done.Task.ConfigureAwait(false);
                if (ex is OperationCanceledException) throw;
                throw new TimeoutException(string.Format(CultureInfo.InvariantCulture,
                    "AutoCAD's main thread did not run the call within {0:0.##} s. A command or a dialog may be " +
                    "active in AutoCAD; finish or cancel it, then call again.", timeout.TotalSeconds));
            }
        }
    }
}
