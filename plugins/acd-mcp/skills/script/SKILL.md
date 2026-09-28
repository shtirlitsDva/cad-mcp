---
name: script
description: Run C# against the drawing that is open in AutoCAD / Civil 3D through ACD-MCP — a live Roslyn session on AutoCAD's main thread, plus a SCRIPT palette editor for scripts the user reviews and runs. Use it whenever the user wants to inspect, count, change, or report on the open drawing, asks about the selected entities, or wants a script to review, even for a one-line question ("what layers are there?"). Load it before the first `autocad_script_*` or `autocad_get_selection` call. For many .dwg files, use /acd-mcp:batch (it covers the sampling calls it needs).
---

<tools>
| Tool | Does | Success result |
|---|---|---|
| `autocad_script_execute(code, timeout_ms?)` | Compiles and runs the snippet now, against the active drawing. Does not touch the editor. | `success, elapsed_ms, return_value_json`; plus `stdout`, `stderr`, `diagnostics` only when not empty |
| `autocad_get_selection()` | Reads the user's current selection (pickfirst) | `document_name, document_path?, count, entities[{handle, object_class, layer, block_name?}]` |
| `autocad_script_propose(name, script_body, input_summary?)` | Saves `%APPDATA%\Acd.Mcp\scripts\script\<name>.csx` and puts it in the SCRIPT editor for the user to review and run | `saved_as, name, replaced_dirty` |

All take an optional `pid` (see /acd-mcp:start).

Two kinds of failure:
- **The snippet ran and failed** — a normal result with `success: false`. `diagnostics` holds compile errors (`severity, message, line, column`); `stderr` holds the exception of a snippet that threw.
- **The snippet did not run** — an error result. Its text starts with a code in brackets, e.g. `[PIPE_NOT_LISTENING]`. With no open drawing it is `[PLUGIN_ERROR] NO_ACTIVE_DOCUMENT: ...`. See /acd-mcp:start `<bring-up>`.

When the user says "look at this" or "the selected one", call `autocad_get_selection` first. It is faster than asking for a handle.
</tools>

<execute-or-propose>
Use `autocad_script_execute` by default: questions, counts, reports, and changes the user asked for directly.

Propose only when:
- the user asks to see, review, or keep the script;
- the script is long (about 30+ lines) or one the user will run again;
- a change is large enough that the user should read it first (many entities, file writes).

Execute does not change the editor. You can keep asking questions with execute while a proposed script waits for the user.
</execute-or-propose>

<snippet-rules>
Globals: `Doc` (active `Document`), `Db` (`Doc.Database`), `Ed` (`Doc.Editor`), `CivilDoc` (`CivilDocument`, or null when the drawing is not a Civil 3D drawing), `Acd` (metadata: `Acd.DataProvider.ReadAll(entity)`, `TryRead(entity, key)`).

Imports: `System`, `System.Collections.Generic`, `System.Linq`, `System.IO`, `System.Text`, `Autodesk.AutoCAD.ApplicationServices`, `.DatabaseServices`, `.Geometry`, `.EditorInput`, `.Runtime`. Others need a `using` or the full name, e.g. `Autodesk.AutoCAD.Colors.Color.FromColorIndex(...)`. Civil 3D namespaces are not imported, because `Autodesk.Civil.DatabaseServices.Entity` collides with the AutoCAD `Entity`. Add them yourself:

```csharp
using AcadEntity = Autodesk.AutoCAD.DatabaseServices.Entity;
using Autodesk.Civil.DatabaseServices;
```

- **`using` directives come first**, before any other statement.
- **Use block-form `using (var tx = ...) { }`** at the top level. A top-level `using var tx = ...;` is a syntax error (CS1002): the script parser reads it as a `using` directive. Inside a method you declare, `using var` works.
- **You open transactions.** The runtime only locks the document. Call `tx.Commit()` for a change; without it the change is rolled back.
- **The session keeps state.** Top-level variables and methods stay for the next call.
- **No `dynamic`** (`Microsoft.CSharp` is not referenced). Use reflection.
- **`Console.WriteLine` goes to `stdout`.** A return value is better for data.
- **`timeout_ms` is cooperative.** A loop that does not check its `CancellationToken` blocks AutoCAD until it ends.
</snippet-rules>

<getting-a-value-back>
The value of the last line comes back when the line is a value, not an action:

| Last line | Comes back? |
|---|---|
| `Doc.Name` or `Doc.Name;` | yes |
| `x * 10;`, `42;`, `new { a = 1 };`, `new int[] { 1, 2 };` | yes |
| `new List<int> { 1, 2 };` (object creation) | yes |
| `layers.ToList();` — **ends in a method call** | **no** |
| `x = 5;`, `i++;`, `await Foo();` | no |
| `return value;` inside a `using (...) { }` block | yes |

The trap is the method call. `lt.Cast<ObjectId>().Select(...).ToList();` returns nothing. Leave out the `;`, or write `return ...;`.
</getting-a-value-back>

<return-values>
`return_value_json` is the value itself (an object, array, or scalar), not a JSON string. Every value goes through a **DTO**: a maintained projection per type, in snake_case.

**Return the entity, not your own anonymous object**, when a DTO exists. A `Line` comes back as `{ start, end, length, layer, color_index }`, the same on every call. DTOs ship for common entities (`Line`, `Polyline`, `Circle`, `Arc`, `BlockReference`, `MText`, `DBText`, `Hatch`, …) and geometry (`Point3d`, `Extents3d`, `ObjectId`, `Handle`, …). Lists and dictionaries of them work.

Use `new { ... }` for a subset (only the layer names of 500 entities), a computed shape, or a type with no DTO in a one-off query.

Markers in the result:
- `{"$unsupported":"Autodesk...Type"}` — no DTO for that type. Project the fields you need, or write a DTO with /acd-mcp:add-dto.
- `{"$serialization_error":"..."}` — a property threw while it was read (often an erased object). Return primitives for it.

Details, the full DTO list, and block attributes / property sets: `references/return-values.md`.
</return-values>

<proposing-a-script>
1. **Read the mirror** `%LOCALAPPDATA%\Acd.Mcp\buffer-script.csx`. It is what the SCRIPT editor shows now. Differences from your last proposal are the user's edits; keep them in your plan.
2. **Call `autocad_script_propose`.** Send only the body; the `// @flavor / @name / @summary` header is written for you. The same `name` overwrites the saved script.
3. **Check `replaced_dirty`:**
   - `false` — the editor had no unsaved edits. Your script is in it now.
   - `true` — the editor had unsaved edits. The user now sees "replace your unsaved changes?". The tool does not return the answer. Tell the user: Yes takes your version, No keeps theirs. Read the mirror again before your next proposal.
4. **Tell the user** the script is in the SCRIPT editor, and that they click Run. There is no tool that runs the editor script; do not run the same body with execute "to check".
</proposing-a-script>

<rules>
1. Verify every type and property name before you use it (/acd-mcp:start `<verify-before-you-reference>`).
2. Execute by default; propose when review has value for the user.
3. Read the mirror before every propose.
4. Script names are short and hyphenated: `transparency-on-x-foobar`.
5. `CivilDoc` can be null. Check it before you use it.
</rules>

<references>
- `references/return-values.md` — DTO list, `Acd.DataProvider` for block attributes and property sets, markers, number edge cases, an information-gathering example.
</references>
