using System.Collections.Generic;

namespace Acd.Mcp.Serialization
{
    // Wire contract for dto.diagnostics (the bridge links this file): one
    // entry per DTO file that failed to compile.
    public sealed record DtoDiagnosticsReport(int Count, IReadOnlyList<DtoDiagnosticEntry> Entries);

    public sealed record DtoDiagnosticEntry(
        string Source,                 // e.g. "user:Circle.csx"
        string HeaderType,             // the // @dto: <Type> string, or "" if no header
        string Message,                // first-diagnostic message
        string? ResolvedType = null,   // null when HeaderType could not be resolved
        int? Line = null,
        int? Column = null,
        string? ErrorCode = null);     // e.g. "CS1061"
}
