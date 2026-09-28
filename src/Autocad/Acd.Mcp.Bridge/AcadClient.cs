using System.IO.Pipes;
using System.Text.Json;
using Mcp.Kernel;
using Mcp.Kernel.Pipe;

namespace Acd.Mcp.Bridge
{
    // Public front for everything Bridge code (MCP tools, host) needs
    // about "talking to AutoCAD". Hides:
    //   - AutoCAD instance discovery (PID enumeration / explicit override
    //     / pipe-probed disambiguation)
    //   - Connect retries across the AutoCAD restart window
    //   - JSON-RPC method names and parameter shapes
    //   - Response envelope unwrapping (error vs result)
    //
    // Discovery + connect both happen per call so AutoCAD restarts (or
    // DevReload reloads) are transparent. The cost is one process
    // enumeration + a 150ms-budget probe per attempt — well under the
    // round-trip time of the snippets the bridge proxies.
    public sealed class AcadClient
    {
        private readonly int? _explicitPid;
        private readonly AutoCadDiscovery _discovery;
        private readonly ConnectRetryPolicy _retry;

        public AcadClient(
            int? explicitPid = null,
            AutoCadDiscovery? discovery = null,
            ConnectRetryPolicy? retry = null)
        {
            _explicitPid = explicitPid;
            _discovery = discovery ?? AutoCadDiscovery.Default;
            _retry = retry ?? ConnectRetryPolicy.Default;
        }

        public async Task<ExecuteResult> ExecuteAsync(
            string code,
            int? timeoutMs,
            int? pid = null,
            CancellationToken ct = default)
        {
            var response = await SendAsync("execute", new { code, timeout_ms = timeoutMs }, pid, ct)
                .ConfigureAwait(false);

            return DecodeResult<ExecuteResult>(response, "execute")
                ?? throw new AcadTransportException(
                    AcadTransportFailure.PipeBroken,
                    "Server returned an empty execute result.");
        }

        // Generic typed call for non-execute methods (batch.*, ping, etc.).
        // Tools / resources use this to invoke the plugin's RPC surface and
        // get a typed result back.
        public async Task<T> CallAsync<T>(string method, object? @params, int? pid = null, CancellationToken ct = default)
        {
            var response = await SendAsync(method, @params, pid, ct).ConfigureAwait(false);
            return DecodeResult<T>(response, method)
                ?? throw new AcadTransportException(
                    AcadTransportFailure.PipeBroken,
                    $"Server returned an empty result for '{method}'.");
        }

        // Retry loop: re-resolve PID per attempt (so the user typing
        // ACDMCP_START mid-retry is picked up), connect with that
        // attempt's deadline, and on success do the one-shot request/
        // response. AcadTransportException with transient reasons triggers
        // another iteration; everything else propagates.
        private async Task<JsonRpcResponse> SendAsync(string method, object? @params, int? requestPid, CancellationToken ct)
        {
            AcadTransportException? lastTransient = null;
            // A per-call pid takes precedence over the bridge-wide --pid, so one
            // bridge can drive several instances by passing pid per tool call.
            int? targetPid = requestPid ?? _explicitPid;

            for (int attempt = 0; attempt < _retry.AttemptTimeoutsMs.Count; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                PidResolution resolution;
                try
                {
                    resolution = await _discovery.ResolveAsync(targetPid, ct).ConfigureAwait(false);
                }
                catch (AcadTransportException ex) when (IsRetryable(ex))
                {
                    lastTransient = ex;
                    await Task.Delay(_retry.AttemptTimeoutsMs[attempt], ct).ConfigureAwait(false);
                    continue;
                }

                if (resolution.IsTransient)
                {
                    // Plugin process is up but pipe isn't listening yet
                    // (drawing-load / ACDMCP_START race). Treat as
                    // transient: wait this attempt's quantum then try
                    // again. The wait doubles as the connect timeout
                    // budget for this iteration.
                    lastTransient = new AcadTransportException(
                        AcadTransportFailure.PipeNotListening,
                        $"AutoCAD PID {resolution.Pid} is up; the acd-mcp listener on pipe " +
                        $"'{PipeProber.PipeNameFor(resolution.Pid)}' is still starting — normal " +
                        "right after the plugin loads. Retry in a few seconds. ACDMCP_START is " +
                        "only needed if the plugin was never loaded into this process.");
                    await Task.Delay(_retry.AttemptTimeoutsMs[attempt], ct).ConfigureAwait(false);
                    continue;
                }

                var pipe = new PipeClient(resolution.Pid);
                NamedPipeClientStream stream;
                try
                {
                    stream = await pipe.ConnectAsync(_retry.AttemptTimeoutsMs[attempt], ct).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    lastTransient = new AcadTransportException(
                        AcadTransportFailure.PipeNotListening,
                        $"Connect to pipe '{pipe.PipeName}' timed out.");
                    continue;
                }

                await using (stream)
                {
                    try
                    {
                        return await pipe.SendOnAsync(stream, method, @params, ct).ConfigureAwait(false);
                    }
                    catch (IOException ex)
                    {
                        // Post-connect I/O failure — pipe was torn down
                        // mid-call (plugin hot-reload, ACDMCP_STOP).
                        // Retry: a fresh connect may land on a freshly
                        // restarted listener.
                        lastTransient = new AcadTransportException(
                            AcadTransportFailure.PipeBroken,
                            $"Pipe '{pipe.PipeName}' broke mid-call: {ex.Message}", ex);
                        continue;
                    }
                    catch (Exception ex) when (ex is JsonException or InvalidDataException)
                    {
                        throw new AcadTransportException(
                            AcadTransportFailure.BadReply,
                            $"Pipe '{pipe.PipeName}' sent a reply to '{method}' that is not a readable frame: {ex.Message}", ex);
                    }
                }
            }

            throw lastTransient ?? new AcadTransportException(
                AcadTransportFailure.PipeNotListening,
                "Connect retries exhausted.");
        }

        private static bool IsRetryable(AcadTransportException ex) =>
            ex.Reason is AcadTransportFailure.PipeNotListening
                      or AcadTransportFailure.PipeBroken;

        private static T? DecodeResult<T>(JsonRpcResponse response, string method)
        {
            if (response.Error is { } err)
                throw new AcadRpcException(err.Code, err.Message);

            if (response.Result is JsonElement el)
            {
                try
                {
                    return el.Deserialize<T>(FrameIO.JsonOptions);
                }
                catch (JsonException ex)
                {
                    throw new AcadTransportException(
                        AcadTransportFailure.BadReply,
                        $"The result of '{method}' does not have the shape of {typeof(T).Name}: {ex.Message}", ex);
                }
            }

            throw new AcadTransportException(
                AcadTransportFailure.PipeBroken,
                "Unexpected response shape (no result, no error).");
        }
    }

    // Transport / protocol errors only. Snippet compile/runtime errors travel
    // inside ExecuteResult (Success=false) and do NOT throw.
    // An McpException, so the plugin's reply reaches the agent as the isError
    // result text, in the same "[ERROR_CODE] detail" form as a transport failure.
    public sealed class AcadRpcException : ModelContextProtocol.McpException
    {
        // The JSON-RPC error code the plugin sent.
        public int Code { get; }

        // The plugin's message without the error-code prefix.
        public string Detail { get; }

        private static string CodeOf(int code) => code switch
        {
            ErrorCodes.MethodNotFound => "METHOD_NOT_FOUND",
            ErrorCodes.InvalidParams  => "INVALID_PARAMS",
            _ => "PLUGIN_ERROR",
        };

        public AcadRpcException(int code, string message) : base($"[{CodeOf(code)}] {message}")
        {
            Code = code;
            Detail = message;
        }
    }
}
