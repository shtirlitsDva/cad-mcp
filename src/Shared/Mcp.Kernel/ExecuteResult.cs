using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mcp.Kernel
{
    // Result of executing a snippet. Lives in the shared kernel because it's the
    // wire currency — every half of both products references it: the in-process
    // plugins produce it, the out-of-process bridges consume it.
    //
    // ReturnValueRepr is the human-display string (.ToString() of the value).
    // ReturnValueJson is the DTO-projected JSON when the value is non-null.
    //
    // ReturnValueRepr is [JsonIgnore] — it never goes on the wire. For the agent
    // it is pure waste: for a string return it is byte-identical to
    // ReturnValueJson, and for any other type ReturnValueJson carries equal-or-
    // richer information (the DTO projection, or a self-describing `$unsupported`
    // / `$serialization_error` marker when the value can't be projected). There
    // is no return shape where Repr tells the agent something Json doesn't, and
    // Json is null only when the value itself is null — so dropping Repr removes
    // a duplicate without opening a blind spot. Its one real consumer is the WPF
    // palette (LogEntryViewModel), which reads this record live in-process and
    // never deserializes JSON, so [JsonIgnore] leaves the palette untouched.
    //
    // Stdout / Stderr / Diagnostics are omitted from the wire when empty
    // (WhenWritingNull + the factories pass null for the empty case) — a
    // side-effect-only snippet shouldn't spend tokens on `"stdout":""` and
    // `"diagnostics":[]`.
    //
    // ReturnValueJson is a JsonElement (not a string of JSON) on purpose: the
    // value is already a JSON value, and every hop that re-serializes
    // ExecuteResult (the pipe frame, then the MCP SDK) embeds a JsonElement raw.
    // Typing it as a string would make those serializers escape every quote —
    // double-encoding the payload into a "..." blob, roughly doubling its size.
    // JsonElement round-trips losslessly across both hops; deserialization clones
    // it so it survives the source document's disposal.
    //
    // Every member that can be null has a default value. The MCP SDK derives
    // the tool's outputSchema from this record, and a positional parameter
    // without a default is "required" there — but a null member is left out on
    // the wire, so a required nullable member makes a normal result fail its
    // own schema.
    public sealed record ExecuteResult(
        bool Success,
        long ElapsedMs,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Stdout = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Stderr = null,
        [property: JsonIgnore] string? ReturnValueRepr = null,
        JsonElement? ReturnValueJson = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DiagnosticInfo[]? Diagnostics = null)
    {
        public static ExecuteResult Ok(string? returnValueRepr, JsonElement? returnValueJson, long elapsedMs) =>
            new(true, elapsedMs, ReturnValueRepr: returnValueRepr, ReturnValueJson: returnValueJson);

        public static ExecuteResult CompileError(DiagnosticInfo[] diagnostics, long elapsedMs) =>
            new(false, elapsedMs, Diagnostics: diagnostics);

        public static ExecuteResult Runtime(string error, long elapsedMs) =>
            new(false, elapsedMs, Stderr: error);
    }

    public sealed record DiagnosticInfo(string Severity, string Message, int? Line = null, int? Column = null);
}
