using System;
using System.Linq;

namespace Acd.Mcp.Batch
{
    // The one mapping from the domain report to its record contract.
    public static class BatchRunReportMapping
    {
        public static BatchRunRecord ToRecord(this BatchRunReport r) => new(
            RunId: r.RunId,
            StartedAt: r.StartedAt,
            CompletedAt: r.CompletedAt,
            RequestedMode: r.RequestedMode,
            Files: r.Files.ToArray(),
            Results: r.Results.Select(ToRecord).ToArray(),
            Cancelled: r.Cancelled,
            AbortedReason: r.AbortedReason);

        private static FileResultRecord ToRecord(BatchFileResult f) => new(
            Path: f.Path,
            Phase: f.Phase,
            Status: f.Status,
            Steps: f.Steps.Select(ToRecord).ToArray(),
            Committed: f.Committed,
            Cancelled: f.Cancelled,
            ElapsedMs: f.ElapsedMs,
            ErrorType: f.Error?.GetType().FullName,
            ErrorMessage: f.Error?.Message);

        private static StepRecord ToRecord(StepOutcome s) => s switch
        {
            StepOutcome.Pass p => new StepRecord(
                StepKind.Pass, p.Name,
                p.Requirements.Select(ToRecord).ToArray(),
                Summary: p.Summary),
            StepOutcome.Failure f => new StepRecord(
                StepKind.Failure, f.Name,
                f.Requirements.Select(ToRecord).ToArray(),
                ErrorType: f.Error.GetType().FullName,
                ErrorMessage: f.Error.Message),
            _ => throw new InvalidOperationException("Unknown StepOutcome subtype: " + s.GetType().FullName),
        };

        private static RequirementRecord ToRecord(RequirementResult r) =>
            new(r.Name, r.Passed, r.Error?.Message);
    }
}
