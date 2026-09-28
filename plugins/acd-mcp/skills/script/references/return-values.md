<contents>
- `<dto-projection>` — how a returned value becomes JSON
- `<shipped-dtos>` — types you can return as they are
- `<metadata>` — block attributes and property sets with `Acd.DataProvider`
- `<markers-and-edge-cases>` — `$unsupported`, `$serialization_error`, NaN
- `<example>` — an information-gathering snippet
</contents>

<dto-projection>
Each value you return goes through the DTO registry before it reaches you. A DTO is a `.csx` file that says which fields of a type go into the JSON. The registry reads:
- `%LOCALAPPDATA%\Acd.Mcp\dto-system\` — shipped with the plugin, replaced on install;
- `%APPDATA%\Acd.Mcp\dto-user\` — the user's own; a DTO here overrides the system DTO for the same type.

Property names become snake_case (`ColorIndex` → `color_index`), in DTOs and in your own anonymous objects.

Why return the entity: the DTO already reads the right properties, reduces AutoCAD types to primitives, and gives the same shape on every call. Your follow-up queries can then rely on the field names.
</dto-projection>

<shipped-dtos>
- Entities: `Arc`, `AttributeReference`, `BlockReference`, `Circle`, `DBPoint`, `DBText`, `Hatch`, `Line`, `MText`, `Polyline`, `Polyline3d`, `PolylineVertex3d`, `Vertex2d`.
- Geometry and ids: `Point2d`, `Point3d`, `Vector2d`, `Vector3d`, `Extents2d`, `Extents3d`, `ObjectId`, `Handle`.
- C# primitives, and collections of all of these (`List<T>`, arrays, `Dictionary<string, T>`, LINQ queries).

The list grows. The folders are the truth: when you are not sure, return the value and look at the result.
</shipped-dtos>

<metadata>
For block attributes and Civil 3D property sets, call `Acd.DataProvider.ReadAll(entity)`. It returns `IReadOnlyDictionary<string, string>` with the values of every metadata source the plugin knows. `Acd.DataProvider.TryRead(entity, key)` returns one value or null.

Why not read attributes yourself: users keep the same data in different places (attributes on one project, property sets on the next). The provider reads all of them; a hand-written reader reads one.

- Plain AutoCAD: block attributes only.
- Civil 3D / Map / MEP: block attributes and AECC property sets.
- XData is not included.

It works with or without your transaction still open: it opens its own short transaction when needed.
</metadata>

<markers-and-edge-cases>
- `{"$unsupported":"Autodesk.AutoCAD.DatabaseServices.RotatedDimension"}` — no DTO for this `Autodesk.*` type. For a one-off, return `new { ... }` with the fields you need. When the user will query this type again, write a DTO (/acd-mcp:add-dto).
- `{"$unsupported":"...", "reason":"compile error in user:<file>.csx (line,col): CS....: ..."}` — a DTO for the type exists but does not compile. Fix it with /acd-mcp:add-dto.
- `{"$serialization_error":"..."}` — reading a property threw (often an erased object). Return primitives for that value.
- `NaN`, `Infinity`, `-Infinity` come back as the strings `"NaN"`, `"Infinity"`, `"-Infinity"`.
</markers-and-edge-cases>

<example>
Count entities per layer in model space, largest first:

```csharp
using (var tx = Db.TransactionManager.StartTransaction())
{
    var ms = (BlockTableRecord)tx.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(Db), OpenMode.ForRead);
    return ms.Cast<ObjectId>()
        .Select(id => (Entity)tx.GetObject(id, OpenMode.ForRead))
        .GroupBy(e => e.Layer)
        .Select(g => new { layer = g.Key, count = g.Count() })
        .OrderByDescending(x => x.count)
        .ToList();
}
```

A subset of fields from many entities is the right case for `new { ... }`. For "show me that polyline", return the `Polyline` itself.
</example>
