using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Acd.Mcp.Bridge.Tools
{
    [McpServerToolType]
    public sealed class BatchListFilesTool
    {
        private readonly AcadClient _client;

        public BatchListFilesTool(AcadClient client)
        {
            _client = client;
        }

        [McpServerTool(
            Name = "autocad_batch_list_files",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false,
            UseStructuredContent = true),
         Description(
            "Return the BATCH palette's current folder + mask + expanded .dwg file list. " +
            "The agent uses this to know exactly which files autocad_batch_run_test would operate on right " +
            "now, to pick representative samples for sideload inspection, and to confirm the user has set " +
            "the right folder + mask before kicking off a Test run. The agent cannot change the file list — " +
            "only the user can, via the palette UI. If the user instead pasted explicit paths into the " +
            "conversation, prefer those and tell the user to match the folder + mask in the palette.")]
        public Task<BatchFilesResult> ListFilesAsync(
            [Description("Optional AutoCAD process id to target. Omit when one instance has the plugin; pass it to pick one when several instances each have Acd.Mcp loaded.")]
            int? pid = null,
            CancellationToken ct = default) =>
            _client.CallAsync<BatchFilesResult>("batch.listFiles", null, pid, ct);
    }

    public sealed record BatchFilesResult(
        string folder,
        string mask,
        bool recurse,
        string[] files,
        int count);
}
