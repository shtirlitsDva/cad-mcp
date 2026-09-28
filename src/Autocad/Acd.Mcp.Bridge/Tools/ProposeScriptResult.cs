namespace Acd.Mcp.Bridge.Tools
{
    // Shared shape returned by BOTH propose-script tools (batch + script)
    // — same approach for like tasks. The plugin side returns identical
    // JSON for batch.proposeScript and script.proposeScript, so a single
    // record is the right abstraction.
    //
    // `replaced_dirty` is the agent's signal that the editor had unsaved
    // typed edits AND those edits differed from the proposed body at the
    // moment of the call — so the user is being prompted (in the WPF
    // dispatcher, asynchronously to the RPC return). The agent should
    // warn the user that their in-flight edits are about to be replaced.
    // The user's actual Yes/No is NOT reported here — it can't be: the
    // dialog is async to the RPC.
    //
    // A failure is an isError result (AcadRpcException / AcadTransportException
    // are McpExceptions), so this shape describes success only.
    public sealed record ProposeScriptResult(
        string saved_as,
        string name,
        bool replaced_dirty);
}
