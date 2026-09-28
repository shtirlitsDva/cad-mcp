using System.Text.Json;

using Acd.Mcp.Batch;
using Acd.Mcp.Bridge.Tools;
using Mcp.Kernel.Pipe;

using Xunit;

namespace Acd.Mcp.Bridge.Tests
{
    public class BatchSetSelectionTests
    {
        private readonly FakePlugin _plugin = new();

        [Fact]
        public async Task SetSelection_SendsFolderMaskAndRecurse_AndReturnsTheNewSelection()
        {
            JsonRpcRequest? seen = null;
            var serve = _plugin.ServeOnceAsync((s, req) =>
            {
                seen = req;
                var selection = new BatchFilesResult(@"C:\drawings", "*_SHT.dwg", true,
                    new[] { @"C:\drawings\001_SHT.dwg" }, 1, BatchOnFailure.Skip);
                return FrameIO.WriteFrameAsync(s, JsonRpcResponse.Ok(req.Id, selection), CancellationToken.None);
            });

            var result = await new BatchSetSelectionTool(_plugin.Client())
                .SetSelectionAsync(@"C:\drawings", "*_SHT.dwg", recurse: true);
            await serve;

            Assert.Equal("batch.setSelection", seen!.Method);
            Assert.Equal(@"C:\drawings", seen.Params.GetProperty("folder").GetString());
            Assert.Equal("*_SHT.dwg", seen.Params.GetProperty("mask").GetString());
            Assert.True(seen.Params.GetProperty("recurse").GetBoolean());
            Assert.Equal(1, result.count);
            Assert.Equal(BatchOnFailure.Skip, result.on_failure);
        }
    }
}
