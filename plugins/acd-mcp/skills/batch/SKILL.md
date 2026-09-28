---
name: batch
description: Apply one C# script to many .dwg files in a folder with the ACD-MCP BATCH runner — Step DSL scripts, Test runs the agent starts, Live runs only the user starts. Use it whenever the user wants the same change, check, or report across several drawings ("every drawing in this folder", "all *_SHT.dwg", "renumber across all sheets"), mentions the BATCH palette or an `autocad_batch_*` tool, or asks why a batch file failed. Load it before the first batch tool call. For one open drawing, use /acd-mcp:script instead.
---

<what-this-skill-is-for>
The BATCH runner opens each drawing of a folder selection as a side `Database` (no document, no editor), runs one script body against it, and records a result per file.

1. **You iterate.** Inspect a sample drawing with `autocad_script_execute`, write the script, propose it to the BATCH editor, set the file selection, run Test, read the results, fix, repeat.
2. **The user goes Live.** When Test passes on every file, tell the user. The user flips the palette switch to Live and clicks Run. Live first runs a full Test pass again, and saves nothing unless every file passes it.

There is no Live tool. Live is always the user's click: it is the safety boundary between a script you wrote and the user's files.

Every tool takes an optional `pid` for when several AutoCADs have `Acd.Mcp` loaded (see /acd-mcp:start).
</what-this-skill-is-for>

<tools>
| Tool | Does | Success result |
|---|---|---|
| `autocad_batch_propose_script(name, script_body, input_summary?)` | Saves `%APPDATA%\Acd.Mcp\scripts\batch\<name>.csx` and puts it in the BATCH editor | `saved_as, name, replaced_dirty` |
| `autocad_batch_set_selection(folder, mask, recurse?)` | Sets the palette's folder + mask and refreshes its file list; opens the palette | same as list_files |
| `autocad_batch_list_files()` | Reads the palette's current selection | `folder, mask, recurse, files, count, on_failure` |
| `autocad_batch_run_test(name?)` | Starts a Test run of the text in the BATCH editor. With `name`, it first proposes that saved script to the editor | `run_id, pending, results_resource, note` |

**`run_test(name)` can run the wrong script.** When the editor has unsaved edits, the proposal only waits for the user's "replace?" answer, and the run starts at once with the user's text. Nothing in the result says so. Use `name` only when the mirror shows the editor holds a saved script (no unsaved edits); otherwise propose, wait for the user, then call `run_test()`.

Resources: `acd-mcp://batch-runs/last`, `acd-mcp://batch-runs/{run_id}`, `acd-mcp://batch-runs/recent{?limit,offset}`.

A failure is an error result. Its text starts with a code in brackets and then the plugin's message, e.g. `[PLUGIN_ERROR] BATCH palette is not open. ...`. Read the message; it says what to do. The common ones:
- palette not open (list_files, run_test) → call `autocad_batch_set_selection`, or ask the user to open it (`ACDMCP_PALETTE`).
- no files selected → call `autocad_batch_set_selection`.
- editor buffer is empty → propose a script first.
- folder not absolute / not found / did not answer within 5 s → a wrong path, or a network drive that is not reachable. Ask the user; do not retry in a loop.

**File selection rules** (`set_selection` and the palette use the same scan):
- Only drawing files are selected: `.dwg`, `.dwt`, `.dws`. A mask can never select `.bak` or other files, also not `*.*`.
- Only `*` and `?` are wildcards. `[` and `]` are plain text.
- Files come in File Explorer order (`3` before `10`).
- `files` is exactly the list your Test run and the user's Live run use.
</tools>

<workflow>
1. **Read the editor mirror** `%LOCALAPPDATA%\Acd.Mcp\buffer-batch.csx`. It is what the user sees in the BATCH editor now. Plan your script against it, so you do not overwrite the user's edits.
2. **Set or confirm the selection.** If the user named a folder and mask, call `autocad_batch_set_selection`. Otherwise call `autocad_batch_list_files` and check that the list is what the user means. The user sees the new selection in the palette at once.
3. **Inspect one or two files from `files`** with `autocad_script_execute` (the active drawing is usually not one of them). Load them as side databases; see `references/script-authoring.md` `<sampling-a-drawing>`. Verify every layer, block, and property name you plan to use.
4. **Write the script body** with the Step DSL (below and `references/script-authoring.md`).
5. **Propose it.** If `replaced_dirty` is `true`, the user had unsaved edits and now sees a "replace your changes?" prompt. Tell them in your reply: Yes takes your version, No keeps theirs (then read the mirror again before your next proposal).
6. **Run Test** with `autocad_batch_run_test()`. It returns at once; the run goes on in AutoCAD.
7. **Wait for the end**, then read `results_resource`. See `references/results.md` for the report shape and for how to wait with the Monitor tool instead of polling.
8. **Fix and repeat** from step 1 until every file passes, or until every failure is one the user expects (a file the script must not touch).
9. **Hand off.** Tell the user the result per file and that the script is ready for Live. Also name the palette's **On failure** choice: **Abort** stops at the first failed file; **Skip** records the failure and goes on. Do not push for the Live click.
</workflow>

<script-body-contract>
Three globals, nothing else:
- `xDb` — the drawing's `Database`, already read from the file.
- `xTx` — an open `Transaction` on `xDb`.
- `ctx` — `IBatchContext`:
  - `Step(name)` — the Step DSL (below).
  - `BatchState<T>()` — state shared across files (`references/script-authoring.md` `<cross-file-state>`).
  - `Phase` (`Test` / `Live`), `Token` (cancellation), `HasFailures` (a step of this file failed).
  - `Fail(reason)` — fails the file without a step; the report shows it as a step named `ctx.Fail`.

Imports: `System`, `System.Collections.Generic`, `System.Linq`, `System.IO`, `System.Text`, `Acd.Mcp.Batch`, `Autodesk.AutoCAD.DatabaseServices`, `Autodesk.AutoCAD.Geometry`, `Autodesk.AutoCAD.Runtime`. There is no `Application`, `Document`, or `Editor`.

The runtime owns the file: it opens it, starts the transaction, and in Live commits and saves (in the file's own DWG version) only when the file passed. Test never saves. So the body has no `new Database`, `ReadDwgFile`, `SaveAs`, `Commit`, outer `try/catch`, or file loop.
</script-body-contract>

<step-dsl>
```csharp
ctx.Step("1. Find title block")
   .Require("Block TITLEBLOCK is defined", () => blocks.Has("TITLEBLOCK"))
   .Apply(() =>
   {
       // read or change xDb through xTx
       return "one short line: what this step did";
   });
```

- Each step is recorded per file with its requirement results and its summary or error. This record is how you and the user see *why* file 7 failed without guessing, so put every assumption in a `Require` and every change in a step.
- A `Require` that returns false, or a `Require` / `Apply` that throws, makes the step a Failure and the file a Failure.
- `Require` is for facts the script needs ("layer exists"). For "do this only when X", write an `if` inside `Apply`.
- A step runs when you call `Apply`. A chain without `Apply` does nothing and is not recorded.
- The palette row shows the **last** step's summary or error. Write requirement names and summaries that make sense alone.

**A failed step does not stop the script.** The next steps still run, on a drawing where the earlier step did nothing. When a step needs the result of an earlier one, stop at the first failure:

```csharp
if (ctx.Step("1. Find title block")
    .Require(...)
    .Apply(() => { titleBlock = ...; return "..."; }) is StepOutcome.Failure) return;
```
</step-dsl>

<side-database-traps>
The drawing is not the one on screen. AutoCAD calls that look things up by name, or that need the text engine, use the drawing on screen instead. The failure is often an error that makes no sense for the file (`eKeyNotFound` on a layer that exists):
- For a new entity, call `SetDatabaseDefaults(xDb)` first, and set `LayerId`, `LinetypeId`, `TextStyleId` from `xDb`'s tables. Do not set `Layer = "NAME"` on an entity that is not yet in `xDb`.
- `GeometricExtents` of text, attributes, or block references with attributes can throw `eInvalidExtents`. Use the extents of geometry (lines, polylines) instead.

More in `references/script-authoring.md` `<side-database-traps>`.
</side-database-traps>

<rules>
1. Live is the user's click. Never ask for a Live tool or say you will run Live.
2. Read the mirror before every propose.
3. Verify names before you use them (see /acd-mcp:start `<verify-before-you-reference>`).
4. Script and step names are short and hyphenated or numbered: `issue-next-revision`, `1. Find title block`.
5. A file that another program has open for writing aborts the whole run, whatever On failure says. Tell the user which file it is.
6. An error from the tool is not a run result. Read the message and fix the cause before you run again.
</rules>

<references>
- `references/script-authoring.md` — sampling a drawing, a full multi-step example, cross-file state with `ctx.BatchState<T>()`, side-database traps, script layout. Read it before you write your first script in a session.
- `references/results.md` — the run report JSON, waiting for a run, reading a failure, common AutoCAD error codes, the editor mirror and `replaced_dirty` in detail. Read it when you read a run result.
</references>
