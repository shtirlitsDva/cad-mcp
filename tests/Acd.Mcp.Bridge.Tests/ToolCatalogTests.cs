using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

using Acd.Mcp.Bridge;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Xunit;

namespace Acd.Mcp.Bridge.Tests
{
    /// <summary>
    /// The tools/list contract Anthropic's mcp-builder guide asks for: an
    /// outputSchema for structured results, annotations that match what each
    /// tool does, and one naming convention on the wire. Each tool is built
    /// through the SDK the same way Program.cs registers it.
    /// </summary>
    public class ToolCatalogTests
    {
        private static readonly Dictionary<string, Tool> Tools = BuildCatalog();

        private static Dictionary<string, Tool> BuildCatalog()
        {
            var client = new AcadClient(explicitPid: null);
            var catalog = new Dictionary<string, Tool>();
            foreach (var type in typeof(AcadClient).Assembly.GetTypes()
                         .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() != null))
            {
                object target = Activator.CreateInstance(type, client)!;
                foreach (var method in type.GetMethods()
                             .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null))
                {
                    var tool = McpServerTool.Create(method, target,
                        new McpServerToolCreateOptions { SerializerOptions = McpServerJson.SnakeCase });
                    catalog[tool.ProtocolTool.Name] = tool.ProtocolTool;
                }
            }
            return catalog;
        }

        public static TheoryData<string> ToolNames() => new(BuildCatalog().Keys);

        [Fact]
        public void Catalog_HasTheSixTools()
        {
            Assert.Equal(
                new[]
                {
                    "autocad_batch_list_files", "autocad_batch_propose_script", "autocad_batch_run_test",
                    "autocad_get_selection", "autocad_script_execute", "autocad_script_propose",
                },
                Tools.Keys.OrderBy(k => k));
        }

        [Theory]
        [MemberData(nameof(ToolNames))]
        public void EveryTool_PublishesAnOutputSchema(string tool)
        {
            Assert.True(Tools[tool].OutputSchema.HasValue, $"{tool} has no outputSchema");
        }

        // A failure is an error result (isError), so a result shape carries
        // no success flag and no error slots for the agent to check.
        [Theory]
        [MemberData(nameof(ToolNames))]
        public void NoResultShape_CarriesErrorSlots(string tool)
        {
            var props = Properties(tool);
            Assert.DoesNotContain("ok", props);
            Assert.DoesNotContain("error_code", props);
            Assert.DoesNotContain("error_message", props);
        }

        // Wire names are snake_case, the convention of the DTO projection
        // (return_value_json) and of every other field the skills name.
        [Theory]
        [MemberData(nameof(ToolNames))]
        public void ResultFields_AreSnakeCase(string tool)
        {
            foreach (string p in Properties(tool))
                Assert.Equal(JsonNamingPolicy.SnakeCaseLower.ConvertName(p), p);
        }

        // Null members are left out on the wire (WhenWritingNull), so a
        // nullable member must not be required — or a normal result such as
        // {"success":true,"elapsed_ms":5} fails its own outputSchema, and a
        // client that validates structuredContent rejects it.
        [Theory]
        [MemberData(nameof(ToolNames))]
        public void NullableMembers_AreNotRequired(string tool)
        {
            var schema = JsonNode.Parse(Tools[tool].OutputSchema!.Value.GetRawText())!;
            foreach (var violation in NullableRequired(schema, tool))
                Assert.Fail(violation);
        }

        private static IEnumerable<string> NullableRequired(JsonNode schema, string path)
        {
            if (schema["properties"] is not JsonObject props) yield break;
            var required = schema["required"]?.AsArray().Select(n => n!.GetValue<string>()).ToHashSet()
                           ?? new HashSet<string>();
            foreach (var (name, prop) in props)
            {
                if (prop is null) continue;
                bool nullable = prop is JsonValue v && v.GetValueKind() == JsonValueKind.True
                                || prop["type"] is JsonArray types && types.Any(t => t!.GetValue<string>() == "null");
                if (nullable && required.Contains(name))
                    yield return $"{path}.{name} can be null (left out on the wire) but is required";
                var nested = prop["items"] ?? prop;
                foreach (var v2 in NullableRequired(nested, $"{path}.{name}"))
                    yield return v2;
            }
        }

        [Fact]
        public void ScriptExecute_ResultNamesMatchItsDescription()
        {
            var props = Properties("autocad_script_execute");
            Assert.Contains("return_value_json", props);
            Assert.Contains("elapsed_ms", props);
        }

        // openWorldHint is for tools that reach outside the server's own
        // domain. Only script_execute runs arbitrary code, which can.
        [Theory]
        [InlineData("autocad_script_execute", true)]
        [InlineData("autocad_script_propose", false)]
        [InlineData("autocad_get_selection", false)]
        [InlineData("autocad_batch_list_files", false)]
        [InlineData("autocad_batch_propose_script", false)]
        [InlineData("autocad_batch_run_test", false)]
        public void OpenWorld_OnlyWhereTheToolReachesOutside(string tool, bool openWorld)
        {
            Assert.Equal(openWorld, Tools[tool].Annotations?.OpenWorldHint);
        }

        [Theory]
        [InlineData("autocad_get_selection")]
        [InlineData("autocad_batch_list_files")]
        public void ReadTools_AreReadOnly(string tool)
        {
            Assert.True(Tools[tool].Annotations?.ReadOnlyHint);
        }

        // With `name` it replaces the BATCH editor buffer, and each call
        // records a run; Test mode rolls back, so no drawing changes.
        [Fact]
        public void BatchRunTest_ChangesStateButDestroysNothing()
        {
            Assert.False(Tools["autocad_batch_run_test"].Annotations?.ReadOnlyHint);
            Assert.False(Tools["autocad_batch_run_test"].Annotations?.DestructiveHint);
        }

        // The SDK passes an McpException's Message to the agent as the
        // isError text, and replaces any other exception's message with a
        // generic one. So the transport failure must be an McpException whose
        // message starts with the error code the skills branch on.
        [Fact]
        public void TransportFailure_ReachesTheAgentWithItsErrorCode()
        {
            var ex = new AcadTransportException(AcadTransportFailure.PipeNotListening, "pipe acd-mcp-42 is down");
            Assert.IsAssignableFrom<ModelContextProtocol.McpException>(ex);
            Assert.Equal("[PIPE_NOT_LISTENING] pipe acd-mcp-42 is down", ex.Message);
            Assert.Equal("pipe acd-mcp-42 is down", ex.Detail);
        }

        [Fact]
        public void PluginRpcFailure_IsAnMcpException()
        {
            Assert.IsAssignableFrom<ModelContextProtocol.McpException>(new AcadRpcException(-32000, "NO_ACTIVE_DOCUMENT: no drawing"));
        }

        private static HashSet<string> Properties(string tool)
        {
            var schema = JsonNode.Parse(Tools[tool].OutputSchema!.Value.GetRawText())!;
            return schema["properties"]!.AsObject().Select(p => p.Key).ToHashSet();
        }
    }
}
