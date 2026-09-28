using Acd.Mcp.Batch.Tests.Fakes;
using Xunit;

namespace Acd.Mcp.Batch.Tests;

// A drawing that cannot be opened (empty or damaged: ReadDwgFile throws
// eBadDwgHeader) is a failure of that one file, not of the whole run.
public class BatchRunnerOpenFailureTests
{
    private const string EmptyBody = "";

    private static (BatchRunner<FakeGlobals> Runner, FakeDrawingHost Host) NewRunner(params string[] unreadable)
    {
        var host = new FakeDrawingHost();
        foreach (var p in unreadable) host.UnreadablePaths.Add(p);
        var runner = new BatchRunner<FakeGlobals>(host, new FakeFileAccessProbe(),
            new BatchScriptHost<FakeGlobals>(TestScriptOptions.Build()));
        return (runner, host);
    }

    [Fact]
    public async Task AFileThatCannotBeOpened_IsAFailureWithTheOpenError()
    {
        var (runner, _) = NewRunner("bad.dwg");
        var reported = new List<BatchFileResult>();

        var report = await runner.RunAsync(EmptyBody, new[] { "bad.dwg" }, BatchMode.Test,
            CancellationToken.None, new SyncProgress(reported.Add));

        var result = Assert.Single(report.Results);
        Assert.Equal(FileOutcomeStatus.Failure, result.Status);
        Assert.IsType<InvalidDataException>(result.Error);
        Assert.Empty(result.Steps);
        Assert.False(result.Committed);
        Assert.Same(result, Assert.Single(reported));
        Assert.Null(report.AbortedReason);
    }

    [Fact]
    public async Task WithAbort_TheRunStopsAtTheFileThatCannotBeOpened()
    {
        var (runner, host) = NewRunner("b.dwg");

        var report = await runner.RunAsync(EmptyBody, new[] { "a.dwg", "b.dwg", "c.dwg" }, BatchMode.Test,
            CancellationToken.None, onFailure: BatchOnFailure.Abort);

        Assert.Equal(new[] { "a.dwg", "b.dwg" }, report.Results.Select(r => r.Path));
        Assert.Equal(new[] { "a.dwg" }, host.OpenedPaths);
    }

    [Fact]
    public async Task WithSkip_TheRunGoesOnAfterTheFileThatCannotBeOpened()
    {
        var (runner, _) = NewRunner("b.dwg");

        var report = await runner.RunAsync(EmptyBody, new[] { "a.dwg", "b.dwg", "c.dwg" }, BatchMode.Test,
            CancellationToken.None, onFailure: BatchOnFailure.Skip);

        Assert.Equal(new[] { FileOutcomeStatus.Pass, FileOutcomeStatus.Failure, FileOutcomeStatus.Pass },
            report.Results.Select(r => r.Status));
    }

    private sealed class SyncProgress : IProgress<BatchFileResult>
    {
        private readonly Action<BatchFileResult> _report;
        public SyncProgress(Action<BatchFileResult> report) => _report = report;
        public void Report(BatchFileResult value) => _report(value);
    }
}
