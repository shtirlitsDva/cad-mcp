using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Acd.Mcp.Bridge.Tools
{
    [McpServerToolType]
    public sealed class ScriptExecuteTool
    {
        private readonly AcadClient _client;

        public ScriptExecuteTool(AcadClient client)
        {
            _client = client;
        }

        // A snippet that fails to compile or throws is a normal result
        // (success=false with diagnostics / stderr): the snippet went through
        // the pipeline and the agent reads why. A bridge that cannot reach the
        // plugin throws AcadTransportException / AcadRpcException — both
        // McpExceptions, so the SDK returns them as isError results with the
        // "[ERROR_CODE] detail" message.
        [McpServerTool(
            Name = "autocad_script_execute",
            ReadOnly = false,        // snippet can modify the drawing
            Destructive = true,      // can erase / overwrite entities
            Idempotent = false,      // session state persists; same call twice ≠ same effect
            OpenWorld = true,        // can touch the file system, network, anything in-process
            UseStructuredContent = true),
         Description(
            "Execute arbitrary C# code inside the running AutoCAD process against the active drawing. " +
            "The snippet runs on AutoCAD's main thread under a document lock. Variables declared at top " +
            "level persist across calls — it's a session, not a one-shot. Globals available: Doc (active " +
            "Document), Db (its Database), Ed (its Editor), CivilDoc (CivilDocument or null), Acd " +
            "(metadata façade). The full Autodesk.AutoCAD.* namespaces are imported (Civil 3D imports " +
            "must be added per-submission). success=false means the snippet did not compile " +
            "(diagnostics) or threw (stderr). return_value_json is the projected value (or a " +
            "$unsupported / $serialization_error marker), never a JSON-encoded string. An error result " +
            "means the plugin was not reached; its text starts with the error code.")]
        public Task<ExecuteResult> ExecuteAsync(
            [Description("C# code to execute. Multi-line allowed; may declare vars/methods; may end with an expression whose value is returned.")]
            string code,
            [Description("Optional cooperative timeout in milliseconds. A snippet that spins without observing its CancellationToken cannot be interrupted.")]
            int? timeout_ms = null,
            [Description("Optional AutoCAD process id to target. Omit when one instance has the plugin; pass it to pick one when several instances each have Acd.Mcp loaded.")]
            int? pid = null,
            CancellationToken ct = default) =>
            _client.ExecuteAsync(code, timeout_ms, pid, ct);
    }
}
