using Acd.Mcp.Pipe;
using Xunit;

namespace Acd.Mcp.Tests;

// MainThread.RunAsync runs work on AutoCAD's main thread (a
// SynchronizationContext) and waits for it with a time limit.
public class MainThreadTests
{
    // Holds posted work until the test runs it, as a busy main thread does.
    private sealed class ManualContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback, object?)> _posted = new();
        public int PostedCount => _posted.Count;
        public override void Post(SendOrPostCallback d, object? state) => _posted.Enqueue((d, state));
        public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException("RunAsync must not block on Send.");
        public void RunAll()
        {
            while (_posted.Count > 0) { var (d, s) = _posted.Dequeue(); d(s); }
        }
    }

    [Fact]
    public async Task RunAsync_ReturnsTheResultOfTheWork()
    {
        var main = new ManualContext();
        var task = MainThread.RunAsync(main, () => 42, TimeSpan.FromSeconds(5), CancellationToken.None);
        main.RunAll();
        Assert.Equal(42, await task);
    }

    [Fact]
    public async Task RunAsync_GivesTheWorksExceptionToTheCaller()
    {
        var main = new ManualContext();
        var task = MainThread.RunAsync<int>(main, () => throw new InvalidOperationException("boom"),
            TimeSpan.FromSeconds(5), CancellationToken.None);
        main.RunAll();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Equal("boom", ex.Message);
    }

    // The caller got a timeout, so the work must not change anything later
    // when the main thread becomes free.
    [Fact]
    public async Task RunAsync_WhenTheMainThreadIsBusy_TimesOutAndTheWorkNeverRuns()
    {
        var main = new ManualContext();
        bool ran = false;
        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            MainThread.RunAsync(main, () => { ran = true; return 0; }, TimeSpan.FromMilliseconds(50), CancellationToken.None));

        Assert.Equal(
            "AutoCAD's main thread did not run the call within 0.05 s. A command or a dialog may be active in AutoCAD; finish or cancel it, then call again.",
            ex.Message);
        main.RunAll();
        Assert.False(ran);
    }

    [Fact]
    public async Task RunAsync_WhenCancelled_TheWorkNeverRuns()
    {
        var main = new ManualContext();
        using var cts = new CancellationTokenSource();
        bool ran = false;
        var task = MainThread.RunAsync(main, () => { ran = true; return 0; }, TimeSpan.FromSeconds(5), cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        main.RunAll();
        Assert.False(ran);
    }
}
