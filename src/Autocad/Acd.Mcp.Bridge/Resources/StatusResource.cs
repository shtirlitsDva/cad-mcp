using System.ComponentModel;
using Acd.Mcp.Pipe;
using ModelContextProtocol.Server;

namespace Acd.Mcp.Bridge.Resources
{
    // MCP resource: acd-mcp://status
    //
    // Returns a snapshot of "what works right now" in the plugin —
    // pipe state and per-capability ready / degraded / unavailable status.
    // Read-only and side-effect-free; safe to poll. Backed by the plugin's
    // acdmcp.status RPC method.
    //
    // It needs a reachable plugin. When the pipe is down there is no
    // capability data, so the read fails like a tool call: an error result
    // with the transport code.
    [McpServerResourceType]
    public sealed class StatusResource
    {
        private readonly AcadClient _client;

        public StatusResource(AcadClient client)
        {
            _client = client;
        }

        [McpServerResource(
            UriTemplate = "acd-mcp://status",
            Name = "acdmcp-status",
            MimeType = "application/json"),
         Description(
            "Live snapshot of the plugin: version, pid, pipe, and per capability " +
            "{ status: ready | degraded | unavailable, reason }. reason names the cause when the " +
            "capability is not ready, e.g. PALETTE_CLOSED. Use it to see which capabilities work " +
            "before a call, or why a call failed. When the pipe is down the read is an error " +
            "result with the transport code (e.g. [PIPE_NOT_LISTENING]). No side effects.")]
        public async Task<string> GetAsync(CancellationToken ct = default) =>
            ResourceJson.Serialize(await _client.CallAsync<StatusSnapshot>(
                "acdmcp.status", new { }, ct: ct).ConfigureAwait(false));
    }
}
