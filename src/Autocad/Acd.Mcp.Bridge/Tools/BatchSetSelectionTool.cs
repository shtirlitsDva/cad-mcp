using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Acd.Mcp.Bridge.Tools
{
    // Agent verb: set the BATCH palette's folder + mask + recurse and refresh
    // its file list, in one call. The palette shows the new selection at
    // once, so the user sees the files before any Live click.
    [McpServerToolType]
    public sealed class BatchSetSelectionTool
    {
        private readonly AcadClient _client;

        public BatchSetSelectionTool(AcadClient client)
        {
            _client = client;
        }

        [McpServerTool(
            Name = "autocad_batch_set_selection",
            ReadOnly = false,       // replaces the palette's selection
            Destructive = false,    // touches no drawing; the old selection can be set again
            Idempotent = true,      // same arguments, same selection
            OpenWorld = false,
            UseStructuredContent = true),
         Description(
            "Set the BATCH palette's folder, mask and recurse, then refresh its file list, in one call. " +
            "Opens the palette if it is closed. Returns the new selection in the autocad_batch_list_files " +
            "shape: this is the file list autocad_batch_run_test and the user's Live run will use. " +
            "The palette keeps its selection when a call fails. Errors: a folder that is not absolute or " +
            "does not exist, an empty or invalid mask, or AutoCAD's main thread busy with a command or " +
            "dialog (the call times out; retry after it ends).")]
        public Task<BatchFilesResult> SetSelectionAsync(
            [Description("Absolute path of the folder that holds the drawings.")]
            string folder,
            [Description("File mask, e.g. *.dwg or *_SHT.dwg. Only * and ? are wildcards; [ ] is plain text. " +
                         "Only drawing files (.dwg, .dwt, .dws) are selected, also with *.*. " +
                         "Files come back in File Explorer order (3 before 10).")]
            string mask,
            [Description("Include subfolders. Default false.")]
            bool recurse = false,
            [Description("Optional AutoCAD process id to target. Omit when one instance has the plugin; pass it to pick one when several instances each have Acd.Mcp loaded.")]
            int? pid = null,
            CancellationToken ct = default) =>
            _client.CallAsync<BatchFilesResult>("batch.setSelection",
                new { folder, mask, recurse }, pid, ct);
    }
}
