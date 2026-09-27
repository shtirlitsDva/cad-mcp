using System.ComponentModel;
using Acd.Mcp.Batch;
using ModelContextProtocol.Server;

namespace Acd.Mcp.Bridge.Resources
{
    // MCP resources for the agent's feedback loop.
    //
    // Three resource templates:
    //   acd-mcp://batch-runs/recent{?limit,offset}
    //   acd-mcp://batch-runs/{run_id}
    //   acd-mcp://batch-runs/last
    //
    // Pagination is mandatory: /recent defaults to limit=20, max=100. The
    // history grows unbounded across plugin restarts; without pagination the
    // agent's context would flood.
    //
    // Each body is a typed contract (RunPage / BatchRunRecord) read from the
    // plugin and written as indented JSON text. A reply of the wrong shape is
    // a BAD_REPLY error, not a partial body.
    [McpServerResourceType]
    public sealed class BatchRunsResource
    {
        private readonly AcadClient _client;

        public BatchRunsResource(AcadClient client)
        {
            _client = client;
        }

        [McpServerResource(
            UriTemplate = "acd-mcp://batch-runs/recent{?limit,offset}",
            Name = "batch-runs-recent",
            MimeType = "application/json"),
         Description(
            "Paginated newest-first list of completed batch runs: limit, offset, total, entries " +
            "(run id, timestamps, requested mode, file / pass / failure counts, cancelled, aborted " +
            "reason), and unreadable (history files that could not be read, with the reason). " +
            "Default limit 20; max 100.")]
        public async Task<string> RecentAsync(
            [Description("Page size. Default 20, max 100.")] int? limit = null,
            [Description("Skip-N. Default 0.")] int? offset = null,
            CancellationToken ct = default) =>
            ResourceJson.Serialize(await _client.CallAsync<RunPage>(
                "batch.listRuns", new { limit, offset }, ct: ct).ConfigureAwait(false));

        [McpServerResource(
            UriTemplate = "acd-mcp://batch-runs/{run_id}",
            Name = "batch-run-by-id",
            MimeType = "application/json"),
         Description(
            "Full per-file result of one batch run. Each file has phase, status (pass | failure), " +
            "error_type / error_message, and steps; each step has kind (pass | failure), name, " +
            "requirements (name, passed), and summary (pass) or error_type / error_message (failure).")]
        public async Task<string> ByIdAsync(
            [Description("The run id returned by autocad_batch_run_test (or from the recent list).")]
            string run_id,
            CancellationToken ct = default)
        {
            // Reserved word: 'last' collides with the alias resource below.
            // The SDK's UriTemplate-based dispatch should pick the alias
            // first when the literal segment matches; we still guard.
            if (string.Equals(run_id, "last", StringComparison.OrdinalIgnoreCase))
                return await LastAsync(ct).ConfigureAwait(false);

            return ResourceJson.Serialize(await _client.CallAsync<BatchRunRecord>(
                "batch.getRun", new { run_id }, ct: ct).ConfigureAwait(false));
        }

        [McpServerResource(
            UriTemplate = "acd-mcp://batch-runs/last",
            Name = "batch-run-last",
            MimeType = "application/json"),
         Description(
            "The most recent batch run, in the same shape as acd-mcp://batch-runs/{run_id}. " +
            "An error when no run exists yet.")]
        public async Task<string> LastAsync(CancellationToken ct = default) =>
            ResourceJson.Serialize(await _client.CallAsync<BatchRunRecord>(
                "batch.getLastRun", new { }, ct: ct).ConfigureAwait(false));
    }
}
