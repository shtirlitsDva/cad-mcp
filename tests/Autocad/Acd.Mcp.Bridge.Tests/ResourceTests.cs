using System.Text.Json;
using System.Text.Json.Nodes;

using Acd.Mcp.Batch;
using Acd.Mcp.Bridge;
using Acd.Mcp.Bridge.Resources;
using Acd.Mcp.Pipe;
using Acd.Mcp.Serialization;
using Mcp.Kernel.Pipe;

using Xunit;

namespace Acd.Mcp.Bridge.Tests
{
    /// <summary>
    /// Each resource reads a typed contract from the plugin and gives the
    /// agent the same convention as the tool results: snake_case names,
    /// enum values as snake_case names, nothing lost.
    /// </summary>
    public class ResourceTests
    {
        // A new instance per test, so each test has its own pipe.
        private readonly FakePlugin _plugin = new();

        [Fact]
        public async Task BatchRunsLast_KeepsEveryStepDetail()
        {
            var serve = _plugin.ServeResultAsync(WireSamples.FailedRun());
            var json = await new BatchRunsResource(_plugin.Client()).LastAsync();
            await serve;

            var run = JsonNode.Parse(json)!;
            AssertSnakeCase(run);
            Assert.Equal("run-1", (string?)run["run_id"]);
            Assert.Equal("test", (string?)run["requested_mode"]);

            var file = run["results"]![0]!;
            Assert.Equal("failure", (string?)file["status"]);
            Assert.Equal("test", (string?)file["phase"]);

            var step = file["steps"]![0]!;
            Assert.Equal("failure", (string?)step["kind"]);
            Assert.Equal("Require 'non-empty' returned false", (string?)step["error_message"]);
            Assert.Equal(false, (bool?)step["requirements"]![1]!["passed"]);
        }

        [Fact]
        public async Task BatchRunsRecent_ListsUnreadableFiles()
        {
            var page = new RunPage(20, 0, 2,
                new[] { new RunSummary("run-1", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, BatchMode.Live, 1, 1, 0, false) },
                new[] { new UnreadableRun("broken.json", "not JSON") });
            var serve = _plugin.ServeResultAsync(page);
            var json = await new BatchRunsResource(_plugin.Client()).RecentAsync();
            await serve;

            var body = JsonNode.Parse(json)!;
            AssertSnakeCase(body);
            Assert.Equal("live", (string?)body["entries"]![0]!["requested_mode"]);
            Assert.Equal("broken.json", (string?)body["unreadable"]![0]!["file"]);
        }

        [Fact]
        public async Task BatchRunsLast_WithNoRun_IsAPluginError()
        {
            var serve = _plugin.ServeErrorAsync(ErrorCodes.InternalError, "No batch run exists yet.");
            var ex = await Assert.ThrowsAsync<AcadRpcException>(
                () => new BatchRunsResource(_plugin.Client()).LastAsync());
            await serve;

            Assert.Equal("[PLUGIN_ERROR] No batch run exists yet.", ex.Message);
        }

        [Fact]
        public async Task Status_NamesEachCapabilityState()
        {
            var ready = new CapabilityState(CapabilityStatus.Ready);
            var snapshot = new StatusSnapshot("2.1.0", _plugin.Pid, "acd-mcp-7000001",
                ready, ready, ready,
                new CapabilityState(CapabilityStatus.Degraded, "PALETTE_CLOSED"), ready, ready, ready);
            var serve = _plugin.ServeResultAsync(snapshot);
            var json = await new StatusResource(_plugin.Client()).GetAsync();
            await serve;

            var body = JsonNode.Parse(json)!;
            AssertSnakeCase(body);
            Assert.Equal("degraded", (string?)body["batch_run_test"]!["status"]);
            Assert.Equal("PALETTE_CLOSED", (string?)body["batch_run_test"]!["reason"]);
            Assert.Equal("ready", (string?)body["dto"]!["status"]);
        }

        // With the pipe down the status resource has no capability data;
        // the error result carries the transport code, as for a tool.
        [Fact]
        public async Task Status_WithNoAutoCad_IsATransportError()
        {
            var acad = new AcadClient(discovery: new NoAutoCad(), retry: new ConnectRetryPolicy(1));
            var ex = await Assert.ThrowsAsync<AcadTransportException>(() => new StatusResource(acad).GetAsync());
            Assert.Equal(AcadTransportFailure.NoAutoCadFound, ex.Reason);
        }

        [Fact]
        public async Task DtoDiagnostics_IsSnakeCase()
        {
            var report = new DtoDiagnosticsReport(1, new[]
            {
                new DtoDiagnosticEntry("user:Circle.csx", "Autodesk.AutoCAD.DatabaseServices.Circle",
                    "'Circle' does not contain a definition for 'CentreOfMass'",
                    ResolvedType: "Autodesk.AutoCAD.DatabaseServices.Circle", Line: 12, Column: 8, ErrorCode: "CS1061"),
            });
            var serve = _plugin.ServeResultAsync(report);
            var json = await new DtoDiagnosticsResource(_plugin.Client()).GetAsync();
            await serve;

            var body = JsonNode.Parse(json)!;
            AssertSnakeCase(body);
            Assert.Equal("CS1061", (string?)body["entries"]![0]!["error_code"]);
            Assert.Equal("Autodesk.AutoCAD.DatabaseServices.Circle", (string?)body["entries"]![0]!["header_type"]);
        }

        private sealed class NoAutoCad : AutoCadDiscovery
        {
            public override int[] FindAutoCadPids() => [];
        }

        private static void AssertSnakeCase(JsonNode node)
        {
            switch (node)
            {
                case JsonObject o:
                    foreach (var (key, value) in o)
                    {
                        Assert.Equal(JsonNamingPolicy.SnakeCaseLower.ConvertName(key), key);
                        if (value is not null) AssertSnakeCase(value);
                    }
                    break;
                case JsonArray a:
                    foreach (var item in a)
                        if (item is not null) AssertSnakeCase(item);
                    break;
            }
        }
    }
}
