using System.Text;

using Acd.Mcp.Bridge;
using Acd.Mcp.Bridge.Tools;
using Acd.Mcp.Pipe;

using Xunit;

namespace Acd.Mcp.Bridge.Tests
{
    /// <summary>
    /// A reply the bridge cannot read is a BAD_REPLY transport failure, so
    /// the agent gets the reason instead of a generic "An error occurred".
    /// </summary>
    public class BadReplyTests
    {
        // A new instance per test, so each test has its own pipe.
        private readonly FakePlugin _plugin = new();

        private static async Task WriteRawFrameAsync(Stream s, byte[] payload)
        {
            int n = payload.Length;
            await s.WriteAsync(new[] { (byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n });
            await s.WriteAsync(payload);
            await s.FlushAsync();
        }

        [Fact]
        public async Task ReplyThatIsNotJson_IsBadReply()
        {
            var serve = _plugin.ServeOnceAsync((s, _) => WriteRawFrameAsync(s, Encoding.UTF8.GetBytes("not json")));

            var ex = await Assert.ThrowsAsync<AcadTransportException>(
                () => _plugin.Client().CallAsync<ProposeScriptResult>("script.proposeScript", null));
            await serve;

            Assert.Equal(AcadTransportFailure.BadReply, ex.Reason);
            Assert.StartsWith("[BAD_REPLY] ", ex.Message);
        }

        [Fact]
        public async Task FrameOverTheSizeLimit_IsBadReply()
        {
            var serve = _plugin.ServeOnceAsync(async (s, _) =>
            {
                await s.WriteAsync(new byte[] { 0x7F, 0xFF, 0xFF, 0xFF });
                await s.FlushAsync();
            });

            var ex = await Assert.ThrowsAsync<AcadTransportException>(
                () => _plugin.Client().CallAsync<ProposeScriptResult>("script.proposeScript", null));
            await serve;

            Assert.Equal(AcadTransportFailure.BadReply, ex.Reason);
        }

        [Fact]
        public async Task ResultOfTheWrongShape_IsBadReply()
        {
            var serve = _plugin.ServeResultAsync("a string, not an object");

            var ex = await Assert.ThrowsAsync<AcadTransportException>(
                () => _plugin.Client().CallAsync<ProposeScriptResult>("script.proposeScript", null));
            await serve;

            Assert.Equal(AcadTransportFailure.BadReply, ex.Reason);
            Assert.Contains("script.proposeScript", ex.Message);
        }
    }
}
