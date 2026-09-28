namespace Acd.Mcp.Pipe
{
    // Wire contract for acdmcp.status (the bridge links this file). The
    // agent branches on each capability's Status; Reason names the cause
    // when the capability is not Ready (e.g. PIPE_NOT_LISTENING,
    // PALETTE_CLOSED, DTO_NOT_READY).
    public sealed record StatusSnapshot(
        string version,
        int pid,
        string pipe,
        CapabilityState script_execute,
        CapabilityState script_propose,
        CapabilityState batch_propose,
        CapabilityState batch_run_test,
        CapabilityState batch_list_files,
        CapabilityState batch_set_selection,
        CapabilityState dto);

    public sealed record CapabilityState(CapabilityStatus status, string? reason = null);

    public enum CapabilityStatus
    {
        Ready,
        Degraded,
        Unavailable,
    }
}
