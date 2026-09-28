using System.Text.Json;
using Xunit;

namespace Acd.Mcp.Batch.Tests;

// BatchRunRecord is the one contract for a completed run: the history file
// and the pipe both carry it. These tests pin the domain -> record mapping,
// the file round trip, and that files from earlier builds keep loading.
public class BatchRunRecordTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "acd-mcp-record-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static readonly DateTimeOffset Stamp = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    // One passing file, one file whose second step failed a Require.
    private static BatchRunReport Report(string runId = "run-1") => new(
        RunId: runId,
        StartedAt: Stamp,
        CompletedAt: Stamp.AddSeconds(2),
        RequestedMode: BatchMode.Test,
        Files: new[] { "a.dwg", "b.dwg" },
        Results: new[]
        {
            new BatchFileResult("a.dwg", BatchPhase.Test, FileOutcomeStatus.Pass,
                new StepOutcome[]
                {
                    new StepOutcome.Pass("set-transparency",
                        new[] { new RequirementResult("layer-exists", true) },
                        "12 entities updated"),
                },
                Committed: false, Cancelled: false, Error: null, ElapsedMs: 7),
            new BatchFileResult("b.dwg", BatchPhase.Test, FileOutcomeStatus.Failure,
                new StepOutcome[]
                {
                    new StepOutcome.Pass("probe", Array.Empty<RequirementResult>(), "ok"),
                    new StepOutcome.Failure("set-transparency",
                        new[]
                        {
                            new RequirementResult("layer-exists", true),
                            new RequirementResult("non-empty", false),
                        },
                        new RequireFailedException("Require 'non-empty' returned false")),
                },
                Committed: false, Cancelled: false,
                Error: new RequireFailedException("Require 'non-empty' returned false"),
                ElapsedMs: 9),
        },
        Cancelled: false,
        AbortedReason: null);

    [Fact]
    public void ToRecord_KeepsEveryStepDetail()
    {
        var record = Report().ToRecord();

        var pass = record.Results[0].Steps.Single();
        Assert.Equal(StepKind.Pass, pass.Kind);
        Assert.Equal("12 entities updated", pass.Summary);
        Assert.Equal(new RequirementRecord("layer-exists", true), pass.Requirements.Single());

        var file = record.Results[1];
        Assert.Equal(FileOutcomeStatus.Failure, file.Status);
        Assert.Equal(typeof(RequireFailedException).FullName, file.ErrorType);
        Assert.Equal("Require 'non-empty' returned false", file.ErrorMessage);

        var failed = file.Steps[1];
        Assert.Equal(StepKind.Failure, failed.Kind);
        Assert.Equal("set-transparency", failed.Name);
        Assert.Null(failed.Summary);
        Assert.Equal(typeof(RequireFailedException).FullName, failed.ErrorType);
        Assert.Equal("Require 'non-empty' returned false", failed.ErrorMessage);
        Assert.Equal(new[] { true, false }, failed.Requirements.Select(r => r.Passed));
    }

    [Fact]
    public void SaveThenLoad_ReturnsTheSameRecord()
    {
        var history = new BatchRunHistory(_root);
        var record = Report().ToRecord();

        history.Save(Report());
        var loaded = history.Load("run-1");

        Assert.NotNull(loaded);
        Assert.Equal(JsonSerializer.Serialize(record), JsonSerializer.Serialize(loaded));
    }

    [Fact]
    public void HistoryFile_WritesEnumNames()
    {
        var history = new BatchRunHistory(_root);
        var text = File.ReadAllText(history.Save(Report()));

        Assert.Contains("\"RequestedMode\": \"Test\"", text);
        Assert.Contains("\"Status\": \"Failure\"", text);
        Assert.Contains("\"Kind\": \"Failure\"", text);
    }

    [Theory]
    [InlineData("2026-05-14_01-48-28_20260514014828-a98edf4e.json", "20260514014828-a98edf4e")]
    [InlineData("2026-05-13_08-25-50_20260513082550-0503e9f8.json", "20260513082550-0503e9f8")]
    public void HistoryFileFromAnEarlierBuild_Loads(string fixture, string runId)
    {
        Directory.CreateDirectory(_root);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture), Path.Combine(_root, fixture));

        var loaded = new BatchRunHistory(_root).Load(runId);

        Assert.NotNull(loaded);
        Assert.Equal(runId, loaded!.RunId);
        Assert.NotEmpty(loaded.Results);
    }

    [Fact]
    public void HistoryFileFromAnEarlierBuild_KeepsStepsAndErrors()
    {
        Directory.CreateDirectory(_root);
        foreach (var f in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures")))
            File.Copy(f, Path.Combine(_root, Path.GetFileName(f)));
        var history = new BatchRunHistory(_root);

        var steps = history.Load("20260514014828-a98edf4e")!.Results[0].Steps.Single();
        Assert.Equal(StepKind.Pass, steps.Kind);
        Assert.Equal("drew 23 entities at (0,0) scale=100", steps.Summary);
        Assert.Equal(new[] { "has-modelspace", "has-layer-table" }, steps.Requirements.Select(r => r.Name));

        var crashed = history.Load("20260513082550-0503e9f8")!.Results[0];
        Assert.Equal("System.InvalidCastException", crashed.ErrorType);
        Assert.StartsWith("[A]Acd.Mcp.Batch.Runtime.AcadBatchGlobals cannot be cast", crashed.ErrorMessage);
    }

    [Fact]
    public void LoadLast_ReturnsTheNewestRun()
    {
        var history = new BatchRunHistory(_root);
        history.Save(Report("old") with { StartedAt = Stamp });
        history.Save(Report("new") with { StartedAt = Stamp.AddMinutes(5) });

        Assert.Equal("new", history.LoadLast()!.RunId);
    }

    [Fact]
    public void LoadLast_WithNoRun_IsNull()
    {
        Assert.Null(new BatchRunHistory(_root).LoadLast());
    }

    // A history file that cannot be read is reported, not hidden.
    [Fact]
    public void ListRecent_ReportsAnUnreadableFile()
    {
        var history = new BatchRunHistory(_root);
        history.Save(Report("good"));
        File.WriteAllText(Path.Combine(_root, "2026-09-27_13-00-00_broken.json"), "not json");

        var page = history.ListRecent(limit: 10, offset: 0);

        Assert.Equal("good", page.Entries.Single().RunId);
        var bad = page.Unreadable.Single();
        Assert.Equal("2026-09-27_13-00-00_broken.json", bad.File);
        Assert.False(string.IsNullOrWhiteSpace(bad.Error));
        Assert.Equal(2, page.Total);
        Assert.Equal(10, page.Limit);
        Assert.Equal(0, page.Offset);
    }
}
