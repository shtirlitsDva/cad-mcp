using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Acd.Mcp.Bridge.Tools
{
    // Agent verb: read the active drawing's pickfirst selection (the
    // entities the user has selected/highlighted in AutoCAD) and return
    // their identifying metadata + the drawing that owns them.
    //
    // This is the "push" channel the user reaches for when they want
    // the LLM to look at a specific entity without having to LIST it
    // and paste the handle into the chat.
    //
    // Annotations:
    //   ReadOnly    = true   (queries live drawing state; no mutation)
    //   Idempotent  = true   (pure read; same pickset => same output)
    //   OpenWorld   = false  (the drawing is the server's own domain)
    [McpServerToolType]
    public sealed class GetSelectionTool
    {
        private readonly AcadClient _client;

        public GetSelectionTool(AcadClient client)
        {
            _client = client;
        }

        [McpServerTool(
            Name = "autocad_get_selection",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false,
            UseStructuredContent = true),
         Description(
            "Return the active drawing's pickfirst selection (entities the user has selected/highlighted " +
            "in AutoCAD) plus the drawing's filename and full path. object_class is the .NET type name " +
            "(e.g. Polyline, BlockReference); block_name is set only for BlockReference entities (the " +
            "user-visible name for dynamic blocks, the BTR name otherwise); document_path is absent for " +
            "unsaved drawings. count=0 with empty entities when nothing is selected. No open drawing is " +
            "an error result that contains NO_ACTIVE_DOCUMENT. Call this when the user says 'look at the " +
            "selected entity' or similar — much faster than asking them to LIST and paste the handle.")]
        public Task<GetSelectionResult> GetSelectionAsync(
            [Description("Optional AutoCAD process id to target. Omit when one instance has the plugin; pass it to pick one when several instances each have Acd.Mcp loaded.")]
            int? pid = null,
            CancellationToken ct = default) =>
            _client.CallAsync<GetSelectionResult>("script.getSelection", null, pid, ct);
    }

    // Nullable members have defaults so the outputSchema does not require
    // them: a null member is left out on the wire.
    public sealed record SelectedEntity(
        string handle,
        string object_class,
        string layer,
        string? block_name = null);

    public sealed record GetSelectionResult(
        string document_name,
        int count,
        SelectedEntity[] entities,
        string? document_path = null);
}
