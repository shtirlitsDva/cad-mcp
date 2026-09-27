using Xunit;

namespace Acd.Mcp.Batch.Tests;

// A run task that ends without a report (it threw, or it was cancelled before
// it started) must still give a report: the palette waits for one to end the
// run, and the history must show that the run happened.
public class UnfinishedRunReportTests
{
    private static readonly DateTimeOffset Started = new(2026, 9, 27, 18, 0, 0, TimeSpan.FromHours(2));
    private static readonly string[] Files = { "a.dwg", "b.dwg" };

    private static BatchFileResult Pass(string path) => new(
        Path: path, Phase: BatchPhase.Test, Status: FileOutcomeStatus.Pass, Steps: Array.Empty<StepOutcome>(),
        Committed: false, Cancelled: false, Error: null, ElapsedMs: 5);

    [Fact]
    public void ARunThatThrew_IsAbortedWithTheError_AndKeepsTheResultsSoFar()
    {
        var soFar = new[] { Pass("a.dwg") };

        var report = BatchRunReport.ForUnfinishedRun("run-1", Started, BatchMode.Test, Files, soFar,
            new InvalidOperationException("eBadDwgHeader"));

        Assert.Equal("run-1", report.RunId);
        Assert.Equal(Started, report.StartedAt);
        Assert.Equal(Files, report.Files);
        Assert.Equal(soFar, report.Results);
        Assert.False(report.Cancelled);
        Assert.Equal("The run failed: eBadDwgHeader", report.AbortedReason);
    }

    [Fact]
    public void ARunCancelledBeforeItStarted_IsCancelled()
    {
        var report = BatchRunReport.ForUnfinishedRun("run-2", Started, BatchMode.Live, Files,
            Array.Empty<BatchFileResult>(), error: null);

        Assert.True(report.Cancelled);
        Assert.Null(report.AbortedReason);
        Assert.Empty(report.Results);
    }
}
