using Acd.Mcp.Batch;
using Acd.Mcp.Batch.Runtime;
using Acd.Mcp.Batch.Ui;
using Acd.Mcp.Pipe;
using Acd.Mcp.Scripting;
#if BRICSCAD
using Bricscad.Windows;
#else
using Autodesk.AutoCAD.Windows;
#endif

namespace Acd.Mcp.Ui
{
    // PaletteSet wrapper. Two tabs:
    //   - SCRIPT — the interactive C# session against the active doc.
    //   - BATCH  — the side-loaded, multi-file batch authoring + runner.
    //
    // Disposes its hosted views synchronously from PaletteSet.Dispose because
    // WPF Unloaded fires asynchronously through the dispatcher — by the time
    // it would arrive, the plugin's executor and log have already been nulled
    // out in McpPlugin.Terminate. Same boundary-isolation pattern as
    // DimensioneringV2.CustomPaletteSet.
    internal sealed class ScriptPaletteSet : PaletteSet
    {
        private static readonly Guid PaletteGuid =
            new("8a4f8d2b-7c5e-4a1f-9c93-3c4b6d2f9a87");

        private readonly ScriptControl _scriptControl;
        private readonly BatchControl _batchControl;

        public ScriptPaletteSet(
            AcadExecutor executor,
            ScriptSession session,
            ExecutionLog log,
            BatchExecutor batchExecutor,
            ScriptEditor scriptScriptEditor)
            : base("ACD-MCP", "ACDMCP_PALETTE", PaletteGuid)
        {
            Style = PaletteSetStyles.ShowAutoHideButton
                  | PaletteSetStyles.ShowCloseButton
                  | PaletteSetStyles.ShowPropertiesMenu
                  | PaletteSetStyles.Snappable;
            MinimumSize = new System.Drawing.Size(420, 320);

            _scriptControl = new ScriptControl(executor, session, log, scriptScriptEditor);
            _batchControl = new BatchControl(batchExecutor);
            AddVisual("SCRIPT", _scriptControl);
            _batchTabIndex = Count;
            AddVisual("BATCH", _batchControl);
        }

        private readonly int _batchTabIndex;

        // Public so the plugin can wire the BATCH VM into the pipe handler
        // as the IBatchUiState provider.
        public BatchViewModel BatchViewModel => _batchControl.ViewModel;

        // For agent calls that change the BATCH tab: the user must see the
        // change, also when the palette was closed or showed SCRIPT.
        public void ShowBatchTab()
        {
            Visible = true;
            Activate(_batchTabIndex);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SafeBoundary.Run("ScriptPaletteSet.Dispose(ScriptControl)", () => _scriptControl.Dispose());
                SafeBoundary.Run("ScriptPaletteSet.Dispose(BatchControl)",  () => _batchControl.Dispose());
            }
            base.Dispose(disposing);
        }
    }
}
