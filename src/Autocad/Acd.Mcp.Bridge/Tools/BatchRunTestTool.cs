using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Acd.Mcp.Bridge.Tools
{
    [McpServerToolType]
    public sealed class BatchRunTestTool
    {
        private readonly AcadClient _client;

        public BatchRunTestTool(AcadClient client)
        {
            _client = client;
        }

        // Not read-only: with `name` it replaces the BATCH editor buffer, and
        // every call records a run. Not destructive: Test mode rolls back, so
        // no drawing is modified.
        [McpServerTool(
            Name = "autocad_batch_run_test",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false,
            UseStructuredContent = true),
         Description(
            "Run a batch script in TEST mode against the BATCH palette's currently-selected folder + mask. " +
            "With NO argument, runs whatever is currently in the live BATCH editor buffer (the common case " +
            "right after autocad_batch_propose_script). With a `name` argument, loads that saved script into " +
            "the editor first and then runs it. Test mode opens each drawing read-shared, runs the script body " +
            "inside a transaction, then rolls back — no file is modified. Returns the run id and its results " +
            "resource URI; read that resource (or acd-mcp://batch-runs/last) for the completed report, OR use " +
            "the Monitor tool to watch %LOCALAPPDATA%\\Acd.Mcp\\log.txt for the line 'BATCH RUN COMPLETED <run_id>'. " +
            "Live execution is intentionally NOT exposed as a tool — the user must flip the slide-switch " +
            "to Live and click Run in person; the runtime auto-runs a Test pass first and refuses Live " +
            "unless every Test file passed.")]
        public Task<BatchRunStartedResult> RunTestAsync(
            [Description("Optional. Saved-script name to load into the editor and run. If omitted, runs whatever the BATCH editor buffer currently holds (the path autocad_batch_propose_script just populated).")]
            string? name = null,
            [Description("Optional AutoCAD process id to target. Omit when one instance has the plugin; pass it to pick one when several instances each have Acd.Mcp loaded.")]
            int? pid = null,
            CancellationToken ct = default) =>
            _client.CallAsync<BatchRunStartedResult>("batch.runTest", new { name }, pid, ct);
    }

    public sealed record BatchRunStartedResult(
        string run_id,
        bool pending,
        string results_resource,
        string note);
}
