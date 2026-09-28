using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

using ModelContextProtocol;

namespace Mcp.Kernel.Bridge
{
    // Single source of truth for the agent-facing MCP JSON policy, shared by the
    // AutoCAD and Revit bridges (both reference this assembly). The stdio
    // channel is a JSON-RPC byte stream, not an HTML document, so the default
    // HTML-safe encoder needlessly escapes '<' '>' '&' '+' backtick and all
    // non-ASCII as \uXXXX (e.g. a generic type name `Dictionary`2` came back
    // mangled). Relax it once, here — both bridges pick it up, so the escaping
    // fix never has to be remembered in two Program.cs files again.
    public static class McpServerJson
    {
        public static JsonSerializerOptions Relaxed { get; } =
            new(McpJsonUtilities.DefaultOptions)
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };

        // The AutoCAD bridge's wire names are snake_case: the DTO projection
        // inside return_value_json is snake_case, and one convention across
        // every result is what the agent (and the outputSchema) can rely on.
        // Without it ExecuteResult alone went out camelCase. Enum values follow
        // the same convention ("test", "failure", "degraded").
        public static JsonSerializerOptions SnakeCase { get; } = CreateSnakeCase();

        private static JsonSerializerOptions CreateSnakeCase()
        {
            var options = new JsonSerializerOptions(Relaxed)
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            };
            // The SDK defaults already hold a JsonStringEnumConverter with
            // PascalCase values, and the first converter that matches a type
            // wins — so this one goes in front of it.
            options.Converters.Insert(0, new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
            return options;
        }
    }
}
