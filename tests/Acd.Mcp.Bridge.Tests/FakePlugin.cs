using System.IO.Pipes;

using Acd.Mcp.Bridge;
using Acd.Mcp.Pipe;

namespace Acd.Mcp.Bridge.Tests
{
    /// <summary>
    /// Stands in for the plugin: listens on the acd-mcp pipe of a made-up
    /// pid and answers one request. Client() gives an AcadClient whose
    /// discovery finds only that pid.
    /// </summary>
    internal sealed class FakePlugin
    {
        private static int _nextPid = 7_000_000;

        // Unique per instance: xUnit runs test classes in parallel, and each
        // fake needs a pipe name of its own.
        public int Pid { get; } = Interlocked.Increment(ref _nextPid);

        private sealed class ListeningProber : PipeProber
        {
            public override Task<bool> IsListeningAsync(int pid, TimeSpan timeout, CancellationToken ct = default)
                => Task.FromResult(true);
        }

        private sealed class OneAutoCad(int pid) : AutoCadDiscovery(new ListeningProber())
        {
            public override int[] FindAutoCadPids() => [pid];
        }

        public AcadClient Client() => new(discovery: new OneAutoCad(Pid), retry: new ConnectRetryPolicy(2000));

        // Answers one request with whatever `reply` writes to the pipe.
        public async Task ServeOnceAsync(Func<Stream, JsonRpcRequest, Task> reply)
        {
            await using var server = new NamedPipeServerStream(
                PipeProber.PipeNameFor(Pid), PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await server.WaitForConnectionAsync();
            var req = await FrameIO.ReadFrameAsync<JsonRpcRequest>(server, CancellationToken.None);
            await reply(server, req!);
        }

        // Answers one request with a result, as the plugin does.
        public Task ServeResultAsync(object result) =>
            ServeOnceAsync((s, req) => FrameIO.WriteFrameAsync(s, JsonRpcResponse.Ok(req.Id, result), CancellationToken.None));

        // Answers one request with a JSON-RPC error, as the plugin does.
        public Task ServeErrorAsync(int code, string message) =>
            ServeOnceAsync((s, req) => FrameIO.WriteFrameAsync(s, JsonRpcResponse.Err(req.Id, code, message), CancellationToken.None));
    }
}
