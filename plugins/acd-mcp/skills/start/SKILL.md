---
name: start
description: Briefing for the ACD-MCP plugin — a live C# script session and a multi-file batch runner inside a running AutoCAD / Civil 3D 2025+. Use it whenever the user mentions AutoCAD, Civil 3D, a .dwg drawing, the SCRIPT or BATCH palette, `autocad_*` tools, or asks what this MCP can do, even when the request looks simple. It says which sibling skill (/acd-mcp:script or /acd-mcp:batch) to load before the first tool call, and how to bring the tools up.
---

<what-this-plugin-is>
A stdio bridge (`Acd.Mcp.Bridge.exe`) talks over the pipe `acd-mcp-<pid>` to a plugin (`Acd.Mcp.dll`) inside AutoCAD 2025+.
- **SCRIPT** — `autocad_script_execute` compiles a C# snippet with Roslyn and runs it on AutoCAD's main thread under `Doc.LockDocument()`, against the active drawing. The session keeps state between calls.
- **BATCH** — one script body applied to each `.dwg` in a folder, each loaded as a side `Database` (no active document).

Every tool takes an optional `pid`. Pass it when `Acd.Mcp` is loaded in more than one AutoCAD; pids come from DevReload's `acad_list_instances`.
</what-this-plugin-is>

<load-a-flavor-first>
Load the flavor skill before the first tool call. Each one holds the rules that stop silent failures (auto-return, mirror-before-propose, `replaced_dirty`, the Step DSL).

| Task | Skill |
|---|---|
| Inspect, change, or report on the drawing that is open | `/acd-mcp:script` |
| The same change across many `.dwg` files in a folder | `/acd-mcp:batch` |
| A result contains `{"$unsupported":"T"}`, or a type needs a richer JSON shape | `/acd-mcp:add-dto` |

One drawing = SCRIPT; many drawings = BATCH. If the intent is not clear, ask the user.
</load-a-flavor-first>

<bring-up>
First call in a session: `autocad_script_execute("Doc.Name")`. It proves the pipe is up and a drawing is open.

A failure is an error result. Its text starts with an error code in brackets, e.g. `[PIPE_NOT_LISTENING] ...`. The bridge already retries the connection (200 / 800 / 2000 ms). The resource `acd-mcp://status` shows the plugin version and which capabilities are ready (e.g. `PALETTE_CLOSED`). It needs the pipe too: when the pipe is down, it gives the same error code.

| Code | Action |
|---|---|
| `PLUGIN_ERROR` | The plugin got the call and refused it. The message after the code says why (e.g. `NO_ACTIVE_DOCUMENT: ...`, `BATCH palette is not open ...`). Act on the message. |
| `INVALID_PARAMS` | A wrong or missing argument. Fix the call. |
| `NO_AUTOCAD_FOUND` | With DevReload: `acad_start`, then `devreload_load_plugin("Acd.Mcp")`. Without: ask the user to start AutoCAD. The plugin opens its pipe on the first idle unless `%LOCALAPPDATA%\Acd.Mcp\config.json` has `{ "auto_start": false }`; then the user runs `ACDMCP_START`. |
| `PIPE_NOT_LISTENING` | The listener is still coming up (normal right after the plugin loads). Wait a few seconds, call again. Still failing without DevReload: ask the user to run `ACDMCP_START`. |
| `AMBIGUOUS_AUTOCADS` | Several AutoCADs, no pipe up yet. Wait, call again. |
| `MULTIPLE_AUTOCAD_PLUGINS` | Pass `pid`. |
| `PINNED_PID_GONE` | The `pid` you passed is no longer running. Get the pids again (`acad_list_instances`). |
| `PIPE_BROKEN` | The connection dropped during the call: AutoCAD closed, crashed, or the plugin was reloaded. Check AutoCAD before you call again; a change may or may not have happened. |
| `BAD_REPLY` | The plugin's reply has the wrong shape. Usually the bridge and the plugin are different versions. Tell the user. |

The propose tools and `autocad_batch_set_selection` open the palette themselves. `autocad_batch_list_files` and `autocad_batch_run_test` need the BATCH palette open with a folder + mask; when it is not, the error text says so — call `autocad_batch_set_selection`, or ask the user to open it (`ACDMCP_PALETTE`).

Civil 3D metadata: `Aec*` assemblies are loaded only in the verticals. Check before you use them:
```csharp
AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).Where(n => n != null && n.StartsWith("Aec")).ToList()
```
</bring-up>

<verify-before-you-reference>
Verify each AutoCAD / Civil 3D property before you use it in a snippet or a DTO. A guessed name fails to compile or returns wrong data, and the user then debugs your guess. In order of preference:
1. Probe the live type: `autocad_script_execute("typeof(T).GetProperties().Select(p => p.Name).ToList()")`.
2. Read the Autodesk .NET API docs (Context7 or a web search for the exact class name) when the type is not reachable from the drawing.
3. Inspect an instance you already have: `obj.GetType().GetProperties()...`.

Still not sure: ask the user.
</verify-before-you-reference>

<file-locations>
| Purpose | Path |
|---|---|
| DTO system folder (plugin-owned, replaced on install) | `%LOCALAPPDATA%\Acd.Mcp\dto-system\` |
| DTO user folder (yours and the user's) | `%APPDATA%\Acd.Mcp\dto-user\` |
| Saved SCRIPT scripts | `%APPDATA%\Acd.Mcp\scripts\script\<name>.csx` |
| Saved BATCH scripts | `%APPDATA%\Acd.Mcp\scripts\batch\<name>.csx` |
| SCRIPT editor mirror | `%LOCALAPPDATA%\Acd.Mcp\buffer-script.csx` |
| BATCH editor mirror | `%LOCALAPPDATA%\Acd.Mcp\buffer-batch.csx` |
| Batch-run history | `%LOCALAPPDATA%\Acd.Mcp\batch-runs\<timestamp>_<run_id>.json` |
| Plugin log | `%LOCALAPPDATA%\Acd.Mcp\log.txt` |
</file-locations>
