using System;
using System.Collections.Generic;

namespace Acd.Mcp.Batch
{
    // The one contract for a batch run. The history file on disk and the pipe
    // to the bridge both carry these records; the bridge links this file.
    //
    // Records only, no behaviour: the domain maps to them one way
    // (BatchRunReportMapping.ToRecord). An exception becomes its type name
    // and message. Enum format is set by each serializer's options, not here.
    //
    // The property names are the history-file names, so files written by
    // earlier builds load unchanged. Keep them stable. Every nullable member
    // has a default: a null member is left out of the JSON, and a file from an
    // earlier build may not have it.

    public sealed record BatchRunRecord(
        string RunId,
        DateTimeOffset StartedAt,
        DateTimeOffset CompletedAt,
        BatchMode RequestedMode,
        IReadOnlyList<string> Files,
        IReadOnlyList<FileResultRecord> Results,
        bool Cancelled,
        // Set when a Live run stopped because its Test pass failed; Results
        // then holds only the Test-phase entries.
        string? AbortedReason = null);

    public sealed record FileResultRecord(
        string Path,
        BatchPhase Phase,
        FileOutcomeStatus Status,
        IReadOnlyList<StepRecord> Steps,
        bool Committed,
        bool Cancelled,
        long ElapsedMs,
        string? ErrorType = null,
        string? ErrorMessage = null);

    public enum StepKind
    {
        Pass,
        Failure,
    }

    // Summary is set for a Pass (the Apply return value); ErrorType and
    // ErrorMessage for a Failure (a false Require or a throw).
    public sealed record StepRecord(
        StepKind Kind,
        string Name,
        IReadOnlyList<RequirementRecord> Requirements,
        string? Summary = null,
        string? ErrorType = null,
        string? ErrorMessage = null);

    // ErrorMessage is set when the predicate threw.
    public sealed record RequirementRecord(
        string Name,
        bool Passed,
        string? ErrorMessage = null);

    public sealed record RunSummary(
        string RunId,
        DateTimeOffset StartedAt,
        DateTimeOffset CompletedAt,
        BatchMode RequestedMode,
        int FileCount,
        int PassCount,
        int FailureCount,
        bool Cancelled,
        string? AbortedReason = null);

    // One page of the history, newest first. Total counts every history
    // file; a file that cannot be read is listed in Unreadable, not hidden.
    public sealed record RunPage(
        int Limit,
        int Offset,
        int Total,
        IReadOnlyList<RunSummary> Entries,
        IReadOnlyList<UnreadableRun> Unreadable);

    public sealed record UnreadableRun(string File, string Error);
}
