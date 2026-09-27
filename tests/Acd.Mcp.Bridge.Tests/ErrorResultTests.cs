using StreamPipe = System.IO.Pipelines.Pipe;
using System.IO.Pipelines;
using System.Reflection;

using Acd.Mcp.Bridge;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Xunit;

namespace Acd.Mcp.Bridge.Tests
{
    /// <summary>
    /// What the agent receives when a call fails: an isError result whose
    /// text holds the bracketed error code the skills branch on.
    /// </summary>
    public class ErrorResultTests
    {
        private sealed class NoAutoCadDiscovery : AutoCadDiscovery
        {
            public override int[] FindAutoCadPids() => [];
        }

        // Full MCP round trip: a real client calls the real tool on a real
        // server over in-memory streams, with discovery that finds no AutoCAD.
        [Fact]
        public async Task TransportFailure_IsAnErrorResultThatContainsItsCode()
        {
            var acad = new AcadClient(
                discovery: new NoAutoCadDiscovery(),
                retry: new ConnectRetryPolicy(1));

            await using var client = await StartServerAsync(acad);
            var result = await client.CallToolAsync(
                "autocad_script_execute", new Dictionary<string, object?> { ["code"] = "1" });

            Assert.True(result.IsError);
            var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
            Assert.Contains("[NO_AUTOCAD_FOUND] ", text);
        }

        [Theory]
        [InlineData(-32601, "METHOD_NOT_FOUND")]
        [InlineData(-32602, "INVALID_PARAMS")]
        [InlineData(-32603, "PLUGIN_ERROR")]
        [InlineData(-32000, "PLUGIN_ERROR")]
        public void PluginFailure_CarriesABracketedCode(int rpcCode, string errorCode)
        {
            var ex = new AcadRpcException(rpcCode, "BATCH palette is not open.");
            Assert.Equal($"[{errorCode}] BATCH palette is not open.", ex.Message);
            Assert.Equal("BATCH palette is not open.", ex.Detail);
            Assert.Equal(rpcCode, ex.Code);
        }

        private static async Task<McpClient> StartServerAsync(AcadClient acad)
        {
            var clientToServer = new StreamPipe();
            var serverToClient = new StreamPipe();

            var tools = new McpServerPrimitiveCollection<McpServerTool>();
            foreach (var type in typeof(AcadClient).Assembly.GetTypes()
                         .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() != null))
            {
                object target = Activator.CreateInstance(type, acad)!;
                foreach (var method in type.GetMethods()
                             .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null))
                    tools.Add(McpServerTool.Create(method, target,
                        new McpServerToolCreateOptions { SerializerOptions = McpServerJson.SnakeCase }));
            }

            var server = McpServer.Create(
                new StreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream()),
                new McpServerOptions { ToolCollection = tools });
            _ = server.RunAsync();

            return await McpClient.CreateAsync(
                new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()));
        }
    }
}
