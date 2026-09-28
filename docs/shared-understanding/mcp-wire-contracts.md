<goal>
Every MCP resource sends a typed, explicit contract. The contract has:
- snake_case field names;
- enum values as names;
- no `Exception` objects;
- no lost fields.

The same rules as the tool results apply. A mismatch on any hop fails loudly (`BAD_REPLY`). It does not drop data.
</goal>

<defects-fixed>
1. `acd-mcp://batch-runs/*`: each step has only `name`. `StepOutcome` is abstract, so the requirements, summary and error are lost.
2. The same resources: enums come out as numbers (`phase: 0`). The pipe options have no enum converter.
3. The same resources: the domain record crosses the pipe with `Exception` members.
4. All resources pass raw camelCase JSON through (`runId`, `elapsedMs`). The tools send snake_case.
5. `acd-mcp://status`: `CapabilityState.status` is a free string ("ready" | "degraded" | "unavailable"). Its comment says "degraded:<reason>", which is stale.
6. `batch.getLastRun` with no run gives a different shape: `{ exists: false }`.
</defects-fixed>

<rules>
- **Enum format:** set in the serializer options of each hop, not on the types.
  - History file: `JsonStringEnumConverter` (already there). Values are PascalCase (`"Test"`).
  - Pipe (`FrameIO.JsonOptions`): add `JsonStringEnumConverter`. It reads names case-insensitively and also reads numbers.
  - MCP output (`McpServerJson.SnakeCase`): add `JsonStringEnumConverter(SnakeCaseLower)`. The agent sees `"test"`, `"pass"`, `"ready"`, the same convention as the field names.
- **Contract types:** records only, with no behaviour. They are source-linked into the bridge, the same way as `ExecuteResult.cs` and `Protocol.cs` today.
- **Domain → contract:** one-way. One mapper, in the module that owns the domain type.
</rules>

<modules>
**Acd.Mcp.Batch**
- New `BatchRunRecord.cs`: `BatchRunRecord`, `FileResultRecord`, `StepRecord`, `RequirementRecord`, `StepKind { Pass, Failure }`, and `RunSummary` (moved here), with `BatchRunRecord.From(BatchRunReport)`.
  - The field names are the same as today's history file, so the 30 existing files load unchanged.
  - The error stays flat (`ErrorType`, `ErrorMessage`). A nested error object would lose the error text of every old file without any warning.
- `FileOutcomeStatus` moves to its own file, so that the bridge can link it.
- `BatchRunHistory`:
  - `Save(BatchRunReport)` writes the record.
  - `Load(runId)` returns `BatchRunRecord?`.
  - `ListRecent` returns summaries from records.
- Delete `ReportEnvelope` and the three `*Envelope` classes, `ToReport`/`ToResult`/`ToOutcome`, and `BatchPersistedError`. Their only use was to rebuild the domain record for the pipe, and that path goes away.

**Acd.Mcp.Contracts**
- `BatchMode.cs` is linked into the bridge (enums only, no change).

**Acd.Mcp (plugin)**
- `Pipe/Protocol.cs`: add the enum converter.
- `Batch/BatchRpcHandler`:
  - `getRun` and `getLastRun` return `BatchRunRecord`.
  - `listRuns` returns a typed `RunPage(limit, offset, total, entries)`.
  - `getLastRun` with no run throws "No batch run exists yet." (see decision 3).
- `Pipe/StatusSnapshot.cs` (split out of `StatusRpcHandler.cs`): `CapabilityState(CapabilityStatus status, string? reason)` with `CapabilityStatus { Ready, Degraded, Unavailable }`.
- `Serialization/DtoDiagnosticsReport.cs`: `DtoDiagnosticsReport(int count, DtoDiagnosticEntry[] entries)`. This replaces the anonymous object in `DtoRpcHandler`.

**Acd.Mcp.Bridge**
- The csproj links the contract files above.
- The resources call `CallAsync<TContract>` and serialize with the MCP snake_case options (indented). `CallRawAsync` and `ResourceJson` go away if nothing else uses them.
- `McpServerJson.SnakeCase`: add the enum converter. The Revit bridge uses `Relaxed`, which does not change.
</modules>

<tests>
- **Batch.Tests:**
  - Map a domain report to a record: a Pass step and a Failure step with requirements, summary, and error type and message.
  - Save, then load, a record.
  - Load a real old history file, copied from disk as a fixture.
  - Enums are names in the file.
- **Bridge.Tests:**
  - `FrameIO` writes enum names and reads numbers.
  - For each resource, a fake pipe server gives the contract. The resource output is snake_case, has `steps[].requirements`, and has `"status": "failure"`.
  - A plugin reply of the wrong shape gives `BAD_REPLY`.
- **Live in Civil 3D:** a Test run with one failing `Require`. Then read `acd-mcp://batch-runs/last` through the new bridge.
</tests>

<decisions-for-user>
1. **Legacy `"Kind": "Skipped"` history entries.** The code still reads them, but 0 of the 30 files on this machine have them.
   - Recommendation: delete the legacy read path. Such a file would then fail to load with a clear error.
2. **`ListRecent` skips a history file that it cannot read, and says nothing** (`catch { }`). This is a silent fallback, and it was there before this change.
   - Recommendation: keep the listing, but add an `unreadable: [{ file, error }]` list to `RunPage`, so that the agent and the user see the bad file.
3. **Two error paths now give an answer that is not an error.**
   - `acd-mcp://status` gives an inline `transport_error` object when the pipe is down.
   - `batch-runs/last` gives `{ exists: false }` when no run exists.
   - Recommendation: both become the usual isError result (`[PIPE_NOT_LISTENING] …`, `[PLUGIN_ERROR] No batch run exists yet.`). Then one error channel exists for tools and resources. When the pipe is down, the status resource cannot report any capability data, so the error code is all that it can give.
</decisions-for-user>
