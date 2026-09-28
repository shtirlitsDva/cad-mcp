---
name: add-dto
description: Write or override a DTO — the small .csx file that tells the ACD-MCP serializer which fields of an AutoCAD / Civil 3D type go into the JSON of a script result. Use it whenever a result contains {"$unsupported":"<type>"} (with or without a "reason"), when a shipped DTO leaves out fields the user needs, when the user asks to "add a DTO" or "make X serialize", or when the same type keeps coming back as a hand-written anonymous object.
---

<what-a-dto-is>
Every value an `autocad_script_execute` snippet returns goes through the DTO registry. A DTO is one C# script file that calls `Acd.RegisterDto<T>(t => new { ... })`. The anonymous object is the JSON shape of `T`. An `Autodesk.*` type without a DTO comes back as `{"$unsupported":"FullTypeName"}`.

A good DTO is worth the effort: every later query of that type gets the same, complete shape for free, so you and the user stop writing one-off projections.
</what-a-dto-is>

<where-dtos-live>
| Folder | Owner | Rule |
|---|---|---|
| `%LOCALAPPDATA%\Acd.Mcp\dto-system\` | the plugin | Replaced on every install. Do not edit: your change is lost at the next install. |
| `%APPDATA%\Acd.Mcp\dto-user\` | the user (and you) | The plugin never writes here. Put all new DTOs and overrides here. |

The loader compiles the system folder first and the user folder second. A `RegisterDto<T>` in the user folder therefore replaces the system DTO for the same `T`.
</where-dtos-live>

<file-rules>
- **One `RegisterDto<T>` per file.** An override replaces a type, not a file; one type per file lets the user override or delete one type without touching others.
- **Name the file after the type, in lowercase**, like the shipped files: `circle.csx`, `rotateddimension.csx`. The loader reads every `*.csx` and does not use the name; the name is for people.
- **Start the file with the header** `// @dto: <full type name>`. When the file does not compile, the loader uses the header to link the error to the type, so the `$unsupported` marker for that type carries the `reason`. Without the header, you only see the error in `acd-mcp://dto-system/diagnostics`.

```csharp
// @dto: Autodesk.AutoCAD.DatabaseServices.Circle

Acd.RegisterDto<Circle>(c => new
{
    center = c.Center,
    radius = c.Radius,
    normal = c.Normal,
    layer = c.Layer,
    color_index = c.Color.ColorIndex,
});
```
</file-rules>

<verify-properties-first>
Verify every property before you put it in a DTO. A wrong name makes the file fail to compile, the type stays `$unsupported`, and the user sees no change. The user has found invented property names before, so do not skip this.

In order of preference:
1. Probe the live type: `autocad_script_execute("typeof(RotatedDimension).GetProperties().Select(p => $\"{p.Name}: {p.PropertyType.Name}\").ToList()")`.
2. Inspect an instance you already have: `obj.GetType().GetProperties()...`.
3. Read the Autodesk .NET API docs (Context7, or a web search for the exact class name).
</verify-properties-first>

<writing-the-projection>
- **Reduce AutoCAD types to primitives at the leaf.** Write `c.Color.ColorIndex` (a `short`), not `c.Color`. Each AutoCAD type in the projection needs its own DTO, so primitives keep the DTO self-contained.
- **Geometry and ids are already covered.** `Point2d`, `Point3d`, `Vector2d`, `Vector3d`, `Extents2d`, `Extents3d`, `ObjectId`, `Handle` ship in the system folder. Use the property as it is.
- **Do not re-write shipped entity DTOs.** `Arc`, `AttributeReference`, `BlockReference`, `Circle`, `DBPoint`, `DBText`, `Hatch`, `Line`, `MText`, `Polyline`, `Polyline3d`, `PolylineVertex3d`, `Vertex2d` ship already. When one is too thin, override it (`<override-a-shipped-dto>`).
- **Names are snake_case.** The serializer converts PascalCase (`ColorIndex` → `color_index`), so either style gives the same JSON. Use the same names as the shipped DTOs for the same idea (`layer`, `color_index`), so follow-up queries work across types.
- **Metadata goes through the data provider:** `attributes = Acd.DataProvider.ReadAll(br)`. It returns the union of block attributes and, on Civil 3D / Map / MEP, AECC property sets (XData is not included). A hand-written reader sees only one of these, and users keep the same data in different places. It opens its own short transaction when needed, so it works after the snippet's transaction has closed.
</writing-the-projection>

<test-it>
A DTO is done only when a real value comes back in the new shape.

1. Save the file in `dto-user\`. No restart and no registration step: the serializer reads the folders again on the next unknown type (at most every 500 ms).
2. Run a snippet that returns a value of the type. Check `return_value_json`.
3. Still `$unsupported`? Read its `reason`, e.g. `compile error in user:rotateddimension.csx (12,8): CS1061: ...`, or read `acd-mcp://dto-system/diagnostics` for every DTO file that does not compile. Fix and repeat.
</test-it>

<override-a-shipped-dto>
1. Copy `%LOCALAPPDATA%\Acd.Mcp\dto-system\<type>.csx` to `%APPDATA%\Acd.Mcp\dto-user\<type>.csx`.
2. Change the copy. The user-folder DTO wins.
3. Test it as above.

Starting from the shipped file keeps the fields that other queries already use.
</override-a-shipped-dto>

<example>
A result contains `{"$unsupported":"Autodesk.AutoCAD.DatabaseServices.RotatedDimension"}`.

1. Probe the properties (see `<verify-properties-first>`). The list shows `Measurement`, `Rotation`, `XLine1Point`, `XLine2Point`, `DimLinePoint`, `Layer`, `Color`, …
2. Write `%APPDATA%\Acd.Mcp\dto-user\rotateddimension.csx`:

```csharp
// @dto: Autodesk.AutoCAD.DatabaseServices.RotatedDimension

Acd.RegisterDto<RotatedDimension>(d => new
{
    measurement = d.Measurement,
    rotation = d.Rotation,
    xline1 = d.XLine1Point,
    xline2 = d.XLine2Point,
    dim_line = d.DimLinePoint,
    layer = d.Layer,
    color_index = d.Color.ColorIndex,
});
```

3. Return a `RotatedDimension` from a snippet and check the shape.
</example>
