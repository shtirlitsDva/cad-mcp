# cad-mcp

Two MCP servers that run C# **inside** a live Autodesk process. The client sends C# code; it compiles with Roslyn, runs on the host's own API thread, and returns the result. State persists between calls — it's a session, not a one-shot.

| Product | Host | Surface | Docs |
|---|---|---|---|
| **acd-mcp** | AutoCAD / Civil 3D 2025+ | C# script session, multi-file batch runner, SCRIPT + BATCH palette — 6 tools, 5 resources | [docs/acd-mcp.md](docs/acd-mcp.md) |
| **rvt-mcp** | Revit 2025+ | C# script session in Revit API context — 1 tool | [docs/rvt-mcp.md](docs/rvt-mcp.md) |

They are **independently versioned and independently released**. Install either without the other.

## Install

Each product has two halves, and they install from different places:

1. **The in-process plugin** — the half that runs inside AutoCAD or Revit. Ships **only in the release zip**, as an Autodesk ApplicationPlugin bundle. Download the product's zip from [Releases](https://github.com/shtirlitsDva/cad-mcp/releases), extract it somewhere permanent, close the host, and run `pwsh install-hooks\Install-Bundle.ps1`. It deploys to `%APPDATA%\Autodesk\ApplicationPlugins\`, which both hosts autoload at startup.

2. **The MCP bridge** — the half your AI client launches. Available from the marketplace:

   ```
   /plugin marketplace add https://github.com/shtirlitsDva/cad-mcp
   /plugin install acd-mcp@cad-mcp     # AutoCAD / Civil 3D
   /plugin install rvt-mcp@cad-mcp     # Revit
   ```

   or from the same extracted zip, for clients without plugin support.

The marketplace cannot carry step 1: it serves `plugins/<product>/` straight from this repo, and the host assemblies are build output, not committed source. Per-product install detail — including the non-Claude clients — is in [docs/acd-mcp.md](docs/acd-mcp.md) and [docs/rvt-mcp.md](docs/rvt-mcp.md).

Both products use the same bundle mechanism, with one difference worth knowing if you touch the manifests: AutoCAD's `ComponentEntry ModuleName` points at a **DLL**, Revit's points at an **`.addin` manifest**. The Revit bundle therefore wraps a `Contents\Rvt.Mcp.addin` rather than replacing it.


## Architecture

Both products are the same two-part shape: an out-of-process **bridge** that speaks MCP over stdio, and an **in-process plugin** loaded into the Autodesk host. They meet on a named pipe.

```
MCP client ─stdio─▶ Acd.Mcp.Bridge.exe ─pipe─▶ AutoCAD (Acd.Mcp.dll)
MCP client ─stdio─▶ Rvt.Mcp.Bridge.exe ─pipe─▶ Revit   (Rvt.Mcp.dll)
                    └──────────────┬──────────────┘
                            Mcp.Kernel (shared)
```

### The shared kernel

`src/Shared/` holds the host-agnostic code both products build on. It is split by **dependency payload**, not by taste — each boundary exists because something must not cross it:

| Project | Contents | Referenced by | Why separate |
|---|---|---|---|
| `Mcp.Kernel` | pipe JSON-RPC framing, `ExecuteResult` envelope | all four halves | zero package references, deliberately — it lands in an AutoCAD process, a Revit process, and both committed `plugins/*/bin` folders |
| `Mcp.Kernel.Scripting` | trailing-expression rewriter, Roslyn `MetadataReference` builder, console capture | the two in-process plugins | carries Roslyn; the bridges must not ship ~15 MB of it for code they never call |
| `Mcp.Kernel.Bridge` | relaxed MCP JSON encoder policy | the two bridges | carries the MCP SDK; the in-process plugins must not load it into AutoCAD's or Revit's ALC |

**The rule: `Autocad/` and `Revit/` may both depend on `Shared/`. Neither may depend on the other.** `tests/Shared/Mcp.Kernel.Tests` enforces the kernel's half of it — it references only the shared projects, so anything that can't be tested without a CAD host doesn't belong in the kernel.

## Repository layout

```
src/
  Shared/     Mcp.Kernel, Mcp.Kernel.Scripting, Mcp.Kernel.Bridge
  Autocad/    Acd.Mcp, Acd.Mcp.Api, Acd.Mcp.Batch, Acd.Mcp.Bridge, Acd.Mcp.Contracts
  Revit/      Rvt.Mcp, Rvt.Mcp.Api, Rvt.Mcp.Bridge, Rvt.Mcp.Loader
tests/
  Shared/     Mcp.Kernel.Tests
  Autocad/    Acd.Mcp.Tests, Acd.Mcp.Batch.Tests, Acd.Mcp.Bridge.Tests
  Revit/      Rvt.Mcp.Tests, Rvt.Mcp.Tests.RequiresRevit
plugins/
  acd-mcp/    plugin manifests, install hooks, skills, committed bridge binaries
  rvt-mcp/    plugin manifests, install hooks, committed bridge binaries
autocad-bundle/ACD-MCP.bundle/   Autodesk ApplicationPlugin manifest; Contents/ filled at release
revit-bundle/RVT-MCP.bundle/     same, plus the committed Contents/Rvt.Mcp.addin
```

## Build and test

```powershell
dotnet build CI.slnf -c Release -p:Platform=x64
dotnet test  CI.slnf -c Release -p:Platform=x64
```

No Autodesk product needs to be installed: AutoCAD 2025, Civil 3D 2025 and Revit 2025 reference assemblies all come from NuGet with `ExcludeAssets="runtime"`.

`CI.slnf` is the full solution minus `tests/Revit/Rvt.Mcp.Tests.RequiresRevit`, the one project that genuinely cannot run headless — it constructs real `Autodesk.Revit.DB` types, and `RevitAPI.dll` P/Invokes into native Revit DLLs no NuGet package can supply. Everything else, both products included, builds and tests on CI.

A solution filter lists projects explicitly, so a project added to `Acd.Mcp.sln` but not `CI.slnf` would silently never be built or tested. `scripts/Test-CIFilterCoverage.ps1` turns that drift into a red build; CI runs it before anything else.

## Releasing

```powershell
pwsh ./scripts/Build-Release.ps1                        # both products
pwsh ./scripts/Build-Release.ps1 -Product rvt -Publish  # one product, and publish it
```

Tags are per product — `acd-v<X.Y.Z>` and `rvt-v<X.Y.Z>` — because the two version independently. Pushing a matching tag makes CI build that product's zip and upload it to the GitHub Release.

## Requirements

* **Windows.** Both hosts are Windows-only.
* **[.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)** for the bridges (they run out-of-process).
* **.NET 8 SDK** to build.

## Safety

Neither product sandboxes anything: arbitrary C# runs in-process with full host API access. These are trusted-developer tools. The pipes rely on the default Windows named-pipe ACL — no custom ACL is set. Don't expose either bridge to an untrusted client.

## License

TBD.
