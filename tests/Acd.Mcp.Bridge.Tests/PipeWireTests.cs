using System.Text.Json;

using Acd.Mcp.Batch;
using Acd.Mcp.Pipe;

using Xunit;

namespace Acd.Mcp.Bridge.Tests
{
    /// <summary>
    /// The pipe serializer (FrameIO.JsonOptions, shared by plugin and bridge)
    /// carries every contract without loss.
    /// </summary>
    public class PipeWireTests
    {
        [Fact]
        public void Enums_CrossThePipeAsNames()
        {
            var json = JsonSerializer.Serialize(new CapabilityState(CapabilityStatus.Degraded, "PALETTE_CLOSED"), FrameIO.JsonOptions);
            Assert.Contains("\"status\":\"Degraded\"", json);
        }

        // A plugin from an earlier build sent enum numbers.
        [Fact]
        public void Enums_AreStillReadFromNumbers()
        {
            var state = JsonSerializer.Deserialize<CapabilityState>("{\"status\":2}", FrameIO.JsonOptions);
            Assert.Equal(CapabilityStatus.Unavailable, state!.status);
        }

        [Fact]
        public void BatchRunRecord_CrossesThePipeWithEveryStepDetail()
        {
            var sent = WireSamples.FailedRun();

            var json = JsonSerializer.Serialize(sent, FrameIO.JsonOptions);
            var received = JsonSerializer.Deserialize<BatchRunRecord>(json, FrameIO.JsonOptions)!;

            var step = received.Results.Single().Steps.Single();
            Assert.Equal(StepKind.Failure, step.Kind);
            Assert.Equal("Require 'non-empty' returned false", step.ErrorMessage);
            Assert.Equal(new[] { "layer-exists", "non-empty" }, step.Requirements.Select(r => r.Name));
        }
    }

    internal static class WireSamples
    {
        public static BatchRunRecord FailedRun() => new(
            RunId: "run-1",
            StartedAt: new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero),
            CompletedAt: new DateTimeOffset(2026, 9, 27, 12, 0, 2, TimeSpan.Zero),
            RequestedMode: BatchMode.Test,
            Files: new[] { "b.dwg" },
            Results: new[]
            {
                new FileResultRecord("b.dwg", BatchPhase.Test, FileOutcomeStatus.Failure,
                    new[]
                    {
                        new StepRecord(StepKind.Failure, "set-transparency",
                            new[] { new RequirementRecord("layer-exists", true), new RequirementRecord("non-empty", false) },
                            ErrorType: "Acd.Mcp.Batch.RequireFailedException",
                            ErrorMessage: "Require 'non-empty' returned false"),
                    },
                    Committed: false, Cancelled: false, ElapsedMs: 9,
                    ErrorType: "Acd.Mcp.Batch.RequireFailedException",
                    ErrorMessage: "Require 'non-empty' returned false"),
            },
            Cancelled: false);
    }
}
