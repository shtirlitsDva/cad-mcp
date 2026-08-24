# rvt-mcp — Revit

[< back to the repo overview](../README.md)

An MCP server that runs C# inside a live Revit process. The client sends C# code; it compiles with Roslyn, runs in Revit API context via an `ExternalEvent` when Revit is idle, and returns the result. State persists between calls.

## How it works

```
MCP client ─stdio─▶ Rvt.Mcp.Bridge.exe ─named pipe─▶ Revit (Rvt.Mcp.dll)
```

* **`Rvt.Mcp.Loader`** — the `.addin`-registered entry point. Deliberately dependency-free: its only job is to load `Rvt.Mcp.dll` into a private `AssemblyLoadContext` so this add-in's Roslyn 4.12 can never collide with whatever Roslyn another add-in preloaded (pyRevit ships 4.11; the version conflict killed the engine on first use before this loader existed).
* **`Rvt.Mcp`** — the engine. Hosts the pipe server (`rvt-mcp-{pid}`) and runs each request through a persistent `CSharpScript` session, marshalled onto Revit's API context with an `ExternalEvent`.
* **`Rvt.Mcp.Api`** — carries `RvtGlobals`, the script-session globals type. Loads into Revit's **default** ALC so Roslyn submissions can bind against it (a non-collectible assembly may not reference a collectible one).
* **`Rvt.Mcp.Bridge`** — stdio MCP server (`net8.0`). Translates MCP calls to JSON-RPC over the pipe and auto-discovers Revit.

The wire protocol, result envelope and Roslyn plumbing are shared verbatim with acd-mcp — see [the shared kernel](../README.md#the-shared-kernel).

## Requirements

* **Revit 2025+.** Revit 2024 and earlier run on .NET Framework and are not supported.
* **[.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)** — the Bridge runs out-of-process and needs it (else `framework not found`).
* **Windows.**
* To build: **.NET 8 SDK**. Revit is not required — references come from the `Nice3point.Revit.Api.*` NuGet packages.

## Build

```powershell
dotnet build src/Revit/Rvt.Mcp.Loader/Rvt.Mcp.Loader.csproj -c Release -p:Platform=x64
dotnet build src/Revit/Rvt.Mcp.Bridge/Rvt.Mcp.Bridge.csproj -c Release -p:Platform=x64
```

The loader pulls the engine in by `ProjectReference`, so one build produces the complete add-in folder (loader + engine + Roslyn). Outputs: `src/Revit/Rvt.Mcp.Loader/bin/Release/` (deploy to Revit) and `src/Revit/Rvt.Mcp.Bridge/bin/Release/Rvt.Mcp.Bridge.exe` (register with your MCP client).

## Install

rvt-mcp has two halves that install separately:

| Half | What it is | Where it comes from |
|---|---|---|
| **Revit add-in** | `Rvt.Mcp.Loader.dll` + engine + Roslyn, autoloaded by Revit | **the release zip** |
| **MCP bridge** | `Rvt.Mcp.Bridge.exe`, launched by your AI client | the plugin marketplace, *or* the same zip |

**The Revit half ships only in the release zip.** The marketplace serves this repo's `plugins/rvt-mcp/` folder, which carries the bridge but not the Revit assemblies — those are build output. The two halves meet on the `rvt-mcp-{pid}` named pipe.

### Step 1 — the Revit add-in (always)

Download `rvt-mcp-plugin-v<X.Y.Z>.zip` from [Releases](https://github.com/shtirlitsDva/cad-mcp/releases) and extract it somewhere permanent. Then, **with Revit closed**:

```powershell
pwsh install-hooks\Install-Bundle.ps1
```

That deploys `RVT-MCP.bundle` into `%APPDATA%\Autodesk\ApplicationPlugins\` — the same mechanism AutoCAD uses. Revit supports it too, with one structural difference worth knowing: where AutoCAD's `PackageContents.xml` points `ComponentEntry ModuleName` at a **DLL**, Revit's points it at an **`.addin` manifest**. So the bundle does not replace the `.addin` file, it *wraps* it — `Contents\Rvt.Mcp.addin` ships inside the bundle.

Consequences: there is no `-RevitYear` to choose (the bundle's `RuntimeRequirements` declares `R2025`–`R2026`), nothing is written into `%APPDATA%\Autodesk\Revit\Addins\<year>\`, and uninstalling is one folder deletion.

Re-run on every upgrade; it refuses to downgrade unless you pass `-Force`.

### Step 2 — the MCP bridge

Pick **one** route. Doing two of them double-registers the server.

**Claude Code:**

```
/plugin marketplace add https://github.com/shtirlitsDva/cad-mcp
/plugin install rvt-mcp@cad-mcp
```

**Codex app:** Settings → Plugins → Add marketplace → `shtirlitsDva/cad-mcp` → install **rvt-mcp**.

**Anything else:** point the client at `bin\Rvt.Mcp.Bridge.exe` in the extracted zip. rvt-mcp has no `Install-Mcp.ps1` of its own yet — acd-mcp's multi-client installer has not been generalised across products.

### From a clone (developers)

`scripts\Deploy-RevitAddin.ps1` builds from source and registers into `%APPDATA%\Autodesk\Revit\Addins\<year>\` instead of deploying a bundle — the faster loop while developing:

```powershell
pwsh scripts\Deploy-RevitAddin.ps1
pwsh scripts\Deploy-RevitAddin.ps1 -RevitYear 2026
```

Don't leave both installed. A per-user add-in registration and a bundle will each load their own copy of the engine, and both will try to open a pipe. `Install-Bundle.ps1` warns when it spots one.

### Uninstall

```powershell
pwsh install-hooks\Uninstall-Bundle.ps1          # remove the Revit bundle
pwsh install-hooks\Uninstall-Bundle.ps1 -Purge   # also delete the log and session state
pwsh scripts\Deploy-RevitAddin.ps1 -Remove       # remove a developer per-user install
```

Claude Code: `/plugin uninstall rvt-mcp@cad-mcp`. Restart Revit afterwards.


## Build a release

```powershell
pwsh ./scripts/Build-Release.ps1 -Product rvt            # → Deploy/rvt-mcp-plugin-v<X.Y.Z>.zip
pwsh ./scripts/Build-Release.ps1 -Product rvt -Publish   # also gh release create + upload
```

The two products in this repo version independently, so releases are tagged per product: `rvt-v<X.Y.Z>` here, `acd-v<X.Y.Z>` for AutoCAD. CI builds + tests on every push; an `rvt-v*` tag also builds this product's zip and uploads it to the GitHub Release. So `git tag rvt-vX.Y.Z && git push --tags` cuts a release.

## MCP surface

One tool.

### `revit_script_execute(code, timeout_ms?)`

Runs C# in Revit API context via `ExternalEvent`, when Revit is idle.

**Globals:** `UiApp` (`UIApplication`), `App` (`Application`), `UiDoc` (`UIDocument` or null), `Doc` (active `Document` or null). All four re-resolve per access, so a document switch between calls is always reflected.

**Default imports:** `System`, `System.Collections.Generic`, `System.Linq`, `System.IO`, `System.Text`, `Autodesk.Revit.DB`, `Autodesk.Revit.UI`.

**Transactions are the snippet's own responsibility.** Model mutations need the snippet to open and commit one:

```csharp
using (var t = new Transaction(Doc, "rename level"))
{
    t.Start();
    Doc.GetElement(new ElementId(123456L)).Name = "L01";
    t.Commit();
}
```

**Return shape** — `ExecuteResult`: `success`, `return_value_json`, `elapsed_ms`, plus `stdout`, `stderr` and `diagnostics` only when non-empty. Compile errors land in `diagnostics`, runtime errors in `stderr`.

**JSON projection** of the returned value:

| Value | Projected as |
|---|---|
| `Element` | `{id, name, category, type}` |
| `ElementId` | number |
| `XYZ` | `{x, y, z}` |
| other `Autodesk.Revit.*` types | `{"$unsupported": "<type>"}` |

Declarations persist across calls — it is a session, not a one-shot.

`timeout_ms` is cooperative. It also bounds how long the Bridge waits for Revit to become idle, which matters because a modal Revit dialog blocks execution entirely.

### Targeting an instance

Unlike the AutoCAD tools, `revit_script_execute` takes no per-call `pid`. With a single Revit that has `Rvt.Mcp` loaded, discovery is automatic. With two or more, pass `--pid <N>` on the Bridge command line to pin one for the session.

Transport error codes (surfaced in `stderr`): `NO_REVIT_FOUND`, `PIPE_NOT_LISTENING`, `AMBIGUOUS_REVITS`, `MULTIPLE_REVIT_PLUGINS`, `PINNED_PID_GONE`, `PIPE_BROKEN`.

## Limitations

* The snippet runs on Revit's API thread. `timeout_ms` cancels at the next `CancellationToken` check; a spin loop can't be interrupted without killing Revit.
* No sandbox — arbitrary C# runs in-process. Trusted-developer tool. The pipe relies on the default Windows named-pipe ACL (no custom ACL is set).
* Modal Revit dialogs block the pipe until closed; `ExternalEvent` never fires while one is up.
* Roslyn-emitted assemblies accumulate over a long session; a Revit restart frees the memory.
* No palette or in-Revit UI, and no batch surface — both exist on the AutoCAD side only. rvt-mcp is `revit_script_execute` and nothing else.

## Compared to acd-mcp

| | acd-mcp | rvt-mcp |
|---|---|---|
| Host | AutoCAD / Civil 3D 2025+ | Revit 2025+ |
| Tools | 6 (script, propose, batch ×3, selection) | 1 (`revit_script_execute`) |
| Resources | 5 | none |
| In-host UI | SCRIPT + BATCH palette | none |
| Multi-file batch | yes | no |
| Custom DTO serialization | yes (user-authored `.csx`) | no (fixed projections) |
| Per-call `pid` targeting | yes | no (`--pid` on the Bridge only) |
| Load mechanism | `.bundle` autoload (or `NETLOAD`) | `.bundle` wrapping an `.addin` + private ALC loader |

## License

TBD.
