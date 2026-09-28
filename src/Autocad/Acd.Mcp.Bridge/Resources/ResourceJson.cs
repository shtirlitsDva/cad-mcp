using System.Text.Json;
using Mcp.Kernel.Bridge;

namespace Acd.Mcp.Bridge.Resources
{
    // Serializer for MCP resource bodies: the agent-facing policy of the tool
    // results (McpServerJson.SnakeCase — relaxed encoder, snake_case names,
    // enum values as snake_case names), indented for reading. One cached
    // instance, because System.Text.Json caches type metadata per options
    // instance.
    internal static class ResourceJson
    {
        private static readonly JsonSerializerOptions Indented =
            new(McpServerJson.SnakeCase) { WriteIndented = true };

        public static string Serialize<T>(T contract) => JsonSerializer.Serialize(contract, Indented);
    }
}
