<contents>
- `<sampling-a-drawing>` — inspect a target file before you write the script
- `<script-layout>` — the order of sections in a script body
- `<full-example>` — a five-step script where each step needs the one before it
- `<cross-file-state>` — `ctx.BatchState<T>()`
- `<side-database-traps>` — AutoCAD calls that fail on a drawing that is not open
- `<do-not>` — things the runtime already does
</contents>

<sampling-a-drawing>
Take one or two paths from `autocad_batch_list_files` / `autocad_batch_set_selection` and read them with `autocad_script_execute`. This is a SCRIPT submission, so its rules apply: `using` directives first, and block-form `using (...) { }` (a top-level `using var` does not parse).

```csharp
using (var sample = new Database(false, true))
{
    // FileShare.ReadWrite, as the runtime uses: AutoCAD can keep the file handle
    // after Dispose, and a Read-only share would make a later Live save fail.
    sample.ReadDwgFile(@"C:\drawings\house-12_SHT.dwg", System.IO.FileShare.ReadWrite, false, "");
    using (var tx = sample.TransactionManager.StartTransaction())
    {
        var layers = (LayerTable)tx.GetObject(sample.LayerTableId, OpenMode.ForRead);
        return layers.Cast<ObjectId>()
            .Select(id => ((LayerTableRecord)tx.GetObject(id, OpenMode.ForRead)).Name)
            .ToList();
    }
}
```

One or two files are a sample, not proof. The Test run over all files is the proof.
</sampling-a-drawing>

<script-layout>
Keep the body flat, in this order:
1. **Inputs** — constants the user may want to change (`const string TargetLayer = "X-FOO";`).
2. **Tables and shared values** — the symbol tables you read, and the variables that one step sets and later steps use.
3. **Steps** — the Step DSL chain.
4. **Helpers** — local functions, at the end. Add one only when two or more places call it.

`autocad_batch_propose_script` writes the `// @flavor / @name / @summary` header for you. Send only the body.
</script-layout>

<full-example>
Issue the next revision of each drawing: find the title block, bump its `REV` attribute, draw a frame around it and write a note on layer `REVISION`, and number the issued drawings across the run. Each step needs what the step before it found, so the script stops at the first failure.

```csharp
var blocks = (BlockTable)xTx.GetObject(xDb.BlockTableId, OpenMode.ForRead);
var layers = (LayerTable)xTx.GetObject(xDb.LayerTableId, OpenMode.ForRead);
var styles = (TextStyleTable)xTx.GetObject(xDb.TextStyleTableId, OpenMode.ForRead);
var modelSpace = (BlockTableRecord)xTx.GetObject(
    SymbolUtilityServices.GetBlockModelSpaceId(xDb), OpenMode.ForRead);

// Set by one step, used by the next ones.
BlockReference titleBlock = null;
Extents3d titleBox = default;
AttributeReference revAttribute = null;
string oldRev = null, newRev = null;

if (ctx.Step("1. Find title block")
    .Require("Block TITLEBLOCK is defined", () => blocks.Has("TITLEBLOCK"))
    .Require("Model space has exactly one TITLEBLOCK", () => TitleBlocks().Count == 1)
    .Apply(() =>
    {
        titleBlock = TitleBlocks().Single();
        // Box from the frame polyline in the definition: extents of the whole
        // reference include the attribute text and can throw eInvalidExtents.
        var definition = (BlockTableRecord)xTx.GetObject(titleBlock.BlockTableRecord, OpenMode.ForRead);
        var frame = definition.Cast<ObjectId>()
            .Select(id => xTx.GetObject(id, OpenMode.ForRead)).OfType<Polyline>().First();
        titleBox = frame.GeometricExtents;
        titleBox.TransformBy(titleBlock.BlockTransform);
        return $"TITLEBLOCK at ({titleBlock.Position.X:0}, {titleBlock.Position.Y:0})";
    }) is StepOutcome.Failure) return;

if (ctx.Step("2. Compute next revision")
    .Require("TITLEBLOCK has a REV attribute", () => Rev() is not null)
    .Require("REV is a letter A-Y, so it can go up by one", () =>
        Rev().TextString is { Length: 1 } r && r[0] >= 'A' && r[0] <= 'Y')
    .Apply(() =>
    {
        revAttribute = Rev();
        oldRev = revAttribute.TextString;
        newRev = ((char)(oldRev[0] + 1)).ToString();
        return $"Revision {oldRev} -> {newRev}";
    }) is StepOutcome.Failure) return;

if (ctx.Step("3. Mark title block on REVISION")
    .Require("Layer REVISION exists", () => layers.Has("REVISION"))
    .Require("Layer REVISION is not locked", () =>
        !((LayerTableRecord)xTx.GetObject(layers["REVISION"], OpenMode.ForRead)).IsLocked)
    .Apply(() =>
    {
        var mark = new Polyline();
        mark.SetDatabaseDefaults(xDb);            // defaults from xDb, not from the screen drawing
        mark.LayerId = layers["REVISION"];        // id, not name
        mark.AddVertexAt(0, new Point2d(titleBox.MinPoint.X - 5, titleBox.MinPoint.Y - 5), 0, 0, 0);
        mark.AddVertexAt(1, new Point2d(titleBox.MaxPoint.X + 5, titleBox.MinPoint.Y - 5), 0, 0, 0);
        mark.AddVertexAt(2, new Point2d(titleBox.MaxPoint.X + 5, titleBox.MaxPoint.Y + 5), 0, 0, 0);
        mark.AddVertexAt(3, new Point2d(titleBox.MinPoint.X - 5, titleBox.MaxPoint.Y + 5), 0, 0, 0);
        mark.Closed = true;
        modelSpace.UpgradeOpen();
        modelSpace.AppendEntity(mark);
        xTx.AddNewlyCreatedDBObject(mark, true);
        return $"Revision frame {mark.Handle} drawn";
    }) is StepOutcome.Failure) return;

if (ctx.Step("4. Write revision note")
    .Require("Text style REVTEXT exists", () => styles.Has("REVTEXT"))
    .Apply(() =>
    {
        var note = new MText();
        note.SetDatabaseDefaults(xDb);
        note.LayerId = layers["REVISION"];
        note.TextStyleId = styles["REVTEXT"];
        note.TextHeight = 4;
        note.Location = new Point3d(titleBox.MinPoint.X, titleBox.MaxPoint.Y + 15, 0);
        note.Contents = $"Rev {newRev}";
        modelSpace.AppendEntity(note);
        xTx.AddNewlyCreatedDBObject(note, true);

        revAttribute.UpgradeOpen();
        revAttribute.TextString = newRev;
        return $"REV set to {newRev}, note {note.Handle} written";
    }) is StepOutcome.Failure) return;

ctx.Step("5. Register issue").Apply(() =>
{
    var issued = ctx.BatchState<List<string>>();
    issued.Add(newRev);
    return $"Issued as #{issued.Count} in this run: rev {oldRev} -> {newRev}";
});

List<BlockReference> TitleBlocks() => modelSpace.Cast<ObjectId>()
    .Where(id => id.ObjectClass.DxfName == "INSERT")
    .Select(id => (BlockReference)xTx.GetObject(id, OpenMode.ForRead))
    .Where(br => br.Name == "TITLEBLOCK")
    .ToList();

AttributeReference Rev() => titleBlock.AttributeCollection.Cast<ObjectId>()
    .Select(id => (AttributeReference)xTx.GetObject(id, OpenMode.ForRead))
    .FirstOrDefault(a => a.Tag == "REV");
```

What the pieces show:
- Symbol tables are opened once at the top and read by many steps.
- `id.ObjectClass.DxfName` filters entities without opening each one.
- Objects are opened for read, and `UpgradeOpen()` is called only right before a change.
- Each requirement name is a full sentence, because the palette shows it alone when it fails.
</full-example>

<cross-file-state>
`ctx.BatchState<T>()` gives the same `T` to every file of one phase. The first call makes it with `new T()`. Use it for counters, running totals, or a list that a report step fills.

```csharp
class SheetCounter { public int Next = 1; }

var counter = ctx.BatchState<SheetCounter>();
ctx.Step("Number sheet").Apply(() => $"Sheet {counter.Next++}");
```

The Test pass and the Live pass each start with **new, empty** state. So Live numbers from the start again, the same as Test did, and the numbers match what Test showed. State does not survive to the next run.

Files run one after another, in the `files` order, so the state needs no lock.
</cross-file-state>

<side-database-traps>
The runner reads each file into a `Database` that no document or editor shows. Some AutoCAD calls use `HostApplicationServices.WorkingDatabase` — the drawing on screen — instead of the database of the object. On a batch file these give wrong results or errors that make no sense for the file.

| Symptom | Cause | Do this |
|---|---|---|
| `eKeyNotFound` when you set `Layer = "X"` on a new entity, and layer X exists in the file | The entity is not in `xDb` yet, so the name is looked up in the drawing on screen | `ent.SetDatabaseDefaults(xDb)`; `ent.LayerId = layers["X"]`. The same for linetype and text style names |
| New entity gets the wrong color, linetype scale, or text style | Defaults come from the drawing on screen | `SetDatabaseDefaults(xDb)` right after `new` |
| `eInvalidExtents` from `GeometricExtents` | Text extents need the text engine, which works on the drawing on screen | Use the extents of lines and polylines, or compute the box from known points |
| `eNotOpenForWrite` | Object opened for read | `UpgradeOpen()` before the change, or open with `OpenMode.ForWrite` |
| `eWasErased` | An erased object | `GetObject(id, OpenMode.ForRead, openErased: false)` and skip `IsErased` ids |

When a call fails only in batch and works in SCRIPT on the same drawing, suspect this list first.
</side-database-traps>

<do-not>
- Do not call `xTx.Commit()`, `xTx.Abort()`, or `xDb.SaveAs(...)`. The runtime commits and saves in Live only when the file passed.
- Do not loop over files. The runtime does.
- Do not wrap the body in `try/catch`. A thrown exception already becomes a step Failure with its message.
- Do not use `Application.DocumentManager`, `Document`, or `Editor`. There is none; the script does not compile.
</do-not>
