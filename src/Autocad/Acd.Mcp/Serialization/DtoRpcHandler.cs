using System.Text.Json;
using Mcp.Kernel.Pipe;

namespace Acd.Mcp.Serialization
{
    // Pipe RPC surface for DTO diagnostics. The pipe listener forwards any
    // method that starts with "dto." to this handler.
    //
    // One method for now:
    //   dto.diagnostics — current list of compile failures, one entry per
    //                     DTO file that didn't successfully compile.
    public sealed class DtoRpcHandler
    {
        private readonly DtoDiagnostics _diagnostics;

        public DtoRpcHandler(DtoDiagnostics diagnostics)
        {
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public Task<object?> DispatchAsync(string method, JsonElement parameters, CancellationToken ct)
        {
            return method switch
            {
                "dto.diagnostics" => Task.FromResult<object?>(GetDiagnostics()),
                _ => Task.FromResult<object?>(null),
            };
        }

        private DtoDiagnosticsReport GetDiagnostics()
        {
            var entries = _diagnostics.All.Select(f => new DtoDiagnosticEntry(
                Source: f.Source,
                HeaderType: f.HeaderType,
                Message: f.Message,
                ResolvedType: f.ResolvedType?.FullName,
                Line: f.Line,
                Column: f.Column,
                ErrorCode: f.ErrorCode)).ToArray();
            return new DtoDiagnosticsReport(entries.Length, entries);
        }
    }
}
