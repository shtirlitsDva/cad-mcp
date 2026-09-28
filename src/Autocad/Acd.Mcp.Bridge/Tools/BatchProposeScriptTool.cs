using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Acd.Mcp.Bridge.Tools
{
    [McpServerToolType]
    public sealed class BatchProposeScriptTool
    {
        private readonly AcadClient _client;

        public BatchProposeScriptTool(AcadClient client)
        {
            _client = client;
        }

        [McpServerTool(
            Name = "autocad_batch_propose_script",
            ReadOnly = false,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false,
            UseStructuredContent = true),
         Description(
            "Save a batch-flavour C# script to %APPDATA%\\Acd.Mcp\\scripts\\batch\\<name>.csx and push it " +
            "into the BATCH palette's live-shared editor. Before calling, READ %LOCALAPPDATA%\\Acd.Mcp\\" +
            "buffer-batch.csx via ordinary file tools to see the editor's current content and plan the " +
            "update against it (so you don't trample user edits). If the editor has dirty changes, the " +
            "user is prompted to confirm before your version replaces theirs (replaced_dirty=true). Same " +
            "name overwrites the existing saved script. See the acd-mcp:batch skill for the full workflow " +
            "and script-body contract. In BricsCAD every path above uses Bcad.Mcp in place of Acd.Mcp.")]
        public Task<ProposeScriptResult> ProposeAsync(
            [Description("Telegram-style name (lowercase, hyphenated, no filler). Used as both the saved filename and the run label.")]
            string name,
            [Description("The script BODY only — no `new Database(...)`, no transactions, no try/catch, no SaveAs. The runtime owns those. Use the Step DSL: ctx.Step(\"name\").Require(\"label\", () => predicate).Apply(() => { ...mutation...; return \"summary\"; });. Globals: xDb (Database), xTx (Transaction), ctx (IBatchContext).")]
            string script_body,
            [Description("Optional one-line summary, surfaced in the Manage Scripts window.")]
            string? input_summary = null,
            [Description("Optional AutoCAD process id to target. Omit when one instance has the plugin; pass it to pick one when several instances each have Acd.Mcp loaded.")]
            int? pid = null,
            CancellationToken ct = default) =>
            _client.CallAsync<ProposeScriptResult>("batch.proposeScript",
                new { name, script_body, input_summary }, pid, ct);
    }
}
