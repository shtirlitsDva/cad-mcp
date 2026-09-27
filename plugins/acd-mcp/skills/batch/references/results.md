<contents>
- `<waiting-for-a-run>` — know when the run has ended
- `<run-report>` — the JSON of `acd-mcp://batch-runs/{run_id}`
- `<reading-a-failure>` — from a failed file to the fix
- `<autocad-error-codes>` — common `error_message` values
- `<run-history>` — `acd-mcp://batch-runs/recent`
- `<editor-mirror-and-replaced-dirty>` — what propose does to the user's editor
</contents>

<waiting-for-a-run>
`autocad_batch_run_test` returns when the run starts, not when it ends. The run is in history only after it ends:
- `acd-mcp://batch-runs/{run_id}` is an error (`No batch run with id ...`) until then.
- `acd-mcp://batch-runs/last` gives the **previous** run until then. Check that its `run_id` is yours.

Wait with the Monitor tool when you have it. The plugin writes one line when a run ends, also a run that was cancelled or failed:

```powershell
Get-Content -Wait -Tail 0 "$env:LOCALAPPDATA\Acd.Mcp\log.txt" | Select-String -SimpleMatch "BATCH RUN COMPLETED <run_id>"
```

The line ends with `(<passed results>/<all results>)`, e.g. `BATCH RUN COMPLETED 20260927185118-a0588b6a (1/5)`. This counts results, not files: an aborted run has fewer results than files, and a Live run has a Test and a Live result per file. Then read `acd-mcp://batch-runs/<run_id>`.

Without Monitor: read `acd-mcp://batch-runs/<run_id>` again after a few seconds, until it is not an error. Each file takes from milliseconds to many seconds, depending on the drawing and the script.
</waiting-for-a-run>

<run-report>
Names and enum values are snake_case. Empty or null fields are left out.

```json
{
  "run_id": "20260927185118-a0588b6a",
  "started_at": "2026-09-27T18:51:18.81+02:00",
  "completed_at": "2026-09-27T18:51:26.27+02:00",
  "requested_mode": "test",
  "files": ["C:\\...\\01_no_titleblock.dwg", "..."],
  "results": [
    {
      "path": "C:\\...\\03_revision_layer_locked.dwg",
      "phase": "test",
      "status": "failure",
      "steps": [
        { "kind": "pass", "name": "1. Find title block",
          "requirements": [ { "name": "Block TITLEBLOCK is defined", "passed": true } ],
          "summary": "TITLEBLOCK at (235, 5)" },
        { "kind": "failure", "name": "3. Mark title block on REVISION",
          "requirements": [
            { "name": "Layer REVISION exists", "passed": true },
            { "name": "Layer REVISION is not locked", "passed": false } ],
          "error_type": "Acd.Mcp.Batch.RequireFailedException",
          "error_message": "Require 'Layer REVISION is not locked' returned false" }
      ],
      "committed": false,
      "cancelled": false,
      "elapsed_ms": 13
    }
  ],
  "cancelled": false
}
```

Run level:
- `requested_mode` — `test` or `live`. A Live run has `test` results for all files first, then `live` results.
- `cancelled` — the user clicked Cancel. `results` holds the files done before that.
- `aborted_reason` — present when the run stopped as a whole: `Compile failed: ...` (no file was opened), `Live pass not started: N file(s) failed the internal Test pass.`, `File '...' is locked or inaccessible. Batch aborted.`, or `The run failed: ...`.
- `results` can be shorter than `files`: On failure = Abort stops at the first failed file.

File level:
- `status` — `pass` or `failure`.
- A requirement also has `error_message` when its predicate threw (not only returned false).
- `error_type` / `error_message` on the **file** (not on a step) — the file itself failed: it could not be opened (an empty or damaged drawing gives `eBadDwgHeader`), the body threw outside any step, or the Live save failed.
- `committed` — `true` only for a Live file that was saved.
</run-report>

<reading-a-failure>
1. Find the first step with `kind: failure`.
2. A requirement with `passed: false` — the file does not match what the script needs. Either the file must not be in the selection (tell the user; narrow the mask), or the script's assumption is wrong (sample that file with `autocad_script_execute` and fix the script).
3. No failed requirement, but `error_message` — `Apply` threw. See `<autocad-error-codes>` and the side-database traps in `script-authoring.md`.
4. Several steps failed on one file — the later failures are usually caused by the first one. Stop the script at the first failure (the `is StepOutcome.Failure) return;` pattern in SKILL.md).
5. Tell the user the result per file in a short table, not the raw JSON.
</reading-a-failure>

<autocad-error-codes>
| `error_message` | Usual cause |
|---|---|
| `eKeyNotFound` | A name that is not in the table. Also: a name set on a new entity before it is in `xDb` (looked up in the drawing on screen). |
| `eInvalidExtents` | `GeometricExtents` on text or on a block with attributes. |
| `eNotOpenForWrite` | A change to an object opened for read. Call `UpgradeOpen()`. |
| `eWasErased` | The object is erased. |
| `eDuplicateRecordName` | A layer / block / style with that name is already there. Check `Has(name)` first. |
| `eBadDwgHeader` (file level) | The file is empty or damaged. Not the script's fault. |
| `eFilerError` (file level, Live) | The save failed; often the file is read-only or open elsewhere. |
</autocad-error-codes>

<run-history>
`acd-mcp://batch-runs/recent{?limit,offset}` lists runs newest first (default `limit` 20, max 100):

```json
{
  "limit": 20, "offset": 0, "total": 52,
  "entries": [
    { "run_id": "20260927185118-a0588b6a", "started_at": "...", "completed_at": "...",
      "requested_mode": "test", "file_count": 5, "pass_count": 1, "failure_count": 4,
      "cancelled": false }
  ],
  "unreadable": []
}
```

`aborted_reason` is in an entry when the run stopped as a whole. `unreadable` lists history files that could not be read, with the reason. History is in `%LOCALAPPDATA%\Acd.Mcp\batch-runs\` and survives AutoCAD restarts.

Use it to find the user's own Live run (`requested_mode: live`) and report what it saved.
</run-history>

<editor-mirror-and-replaced-dirty>
`%LOCALAPPDATA%\Acd.Mcp\buffer-batch.csx` always holds what the BATCH editor shows. While the user types, it is written about 250 ms after the last key. After an accepted proposal, it is written before the tool returns.

`autocad_batch_propose_script`:
- The editor has no unsaved edits → your body goes into the editor at once. `replaced_dirty: false`.
- The editor has unsaved edits that differ from your body → the editor keeps the user's text and shows "replace your unsaved changes?". `replaced_dirty: true`. The tool returns before the user answers, and does not tell you the answer. Read the mirror before your next step to see which text won.

In both cases the script is saved to `%APPDATA%\Acd.Mcp\scripts\batch\<name>.csx`. The same name overwrites.

`autocad_batch_run_test` always runs what the editor holds when the run starts:
- After `replaced_dirty: true`, run only after the user has answered.
- `run_test(name)` proposes the saved script and starts the run in the same call. When the editor has unsaved edits, the proposal waits for the user and the run uses the user's text. Use `name` only when the editor has no unsaved edits.
</editor-mirror-and-replaced-dirty>
