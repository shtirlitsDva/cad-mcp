using System;
using System.Collections.Generic;

namespace Acd.Mcp.Batch
{
    // One per file processed in a phase. The Steps list is the per-step
    // breakdown; Status is the rolled-up outcome.
    public sealed record BatchFileResult(
        string Path,
        BatchPhase Phase,
        FileOutcomeStatus Status,
        IReadOnlyList<StepOutcome> Steps,
        bool Committed,
        bool Cancelled,
        Exception? Error,
        long ElapsedMs);

    // One per complete batch run (one or two phases). Newest-first when
    // listed; written to disk by BatchRunHistory.
    public sealed record BatchRunReport(
        string RunId,
        DateTimeOffset StartedAt,
        DateTimeOffset CompletedAt,
        BatchMode RequestedMode,
        IReadOnlyList<string> Files,
        IReadOnlyList<BatchFileResult> Results,
        bool Cancelled,
        // Live runs that aborted because the internal Test pass failed will have
        // this set; the Results list then contains only the Test-phase entries.
        string? AbortedReason)
    {
        // For a run task that ended without a report: it threw (`error`), or
        // it was cancelled before it started (`error` null). The palette
        // needs a report to end the run, and the history keeps the results
        // that came in before the end.
        public static BatchRunReport ForUnfinishedRun(
            string runId, DateTimeOffset startedAt, BatchMode mode, IReadOnlyList<string> files,
            IReadOnlyList<BatchFileResult> resultsSoFar, Exception? error) => new(
                RunId: runId,
                StartedAt: startedAt,
                CompletedAt: DateTimeOffset.Now,
                RequestedMode: mode,
                Files: files,
                Results: resultsSoFar,
                Cancelled: error is null,
                AbortedReason: error is null ? null : $"The run failed: {error.Message}");
    }
}
