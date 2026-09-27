namespace Acd.Mcp.Batch
{
    // Per-file outcome status — the only two flavours the UI/agent see.
    //
    // Pass: every step's StepOutcome was Pass AND no exception escaped the
    //       body. In Live mode this is the only state that commits.
    //
    // Failure: any StepOutcome.Failure (including a false Require predicate —
    //       Require is a hard precondition), any uncaught exception from the
    //       script body, or ctx.Fail() was called. The file's transaction is
    //       rolled back; the loop's behavior on this file follows the user's
    //       "On failure" palette choice (Abort | Skip).
    //
    // (Cancelled is a separate orthogonal flag — see BatchFileResult.Cancelled.
    //  A cancelled file is reported but the loop exits.)
    public enum FileOutcomeStatus
    {
        Pass,
        Failure,
    }
}
