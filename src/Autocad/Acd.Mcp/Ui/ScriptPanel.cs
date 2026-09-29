#if BRICSCAD
using System.Windows.Controls;
using Acd.Mcp.Batch;
using Acd.Mcp.Batch.Runtime;
using Acd.Mcp.Batch.Ui;
using Acd.Mcp.Pipe;
using Acd.Mcp.Scripting;
using Bricscad.Windows;
using Panel = Bricscad.Windows.Panel;

namespace Acd.Mcp.Ui
{
    // BricsCAD's container for the SCRIPT and BATCH views: one native Panel in
    // the right-hand dock stack (RDOCK, with Properties and Layers), with our
    // own side tabs inside (SideTabHost), since a Panel has none. AutoCAD gets
    // ScriptPaletteSet instead. BricsCAD's PaletteSet is a compatibility shim:
    // docked it loses auto-hide and mashes the tabs.
    //
    // Created when the plugin loads (McpPlugin.Initialize), so its icon is on
    // the stack from the start; ACDMCP_PALETTE only brings it forward.
    //
    // A Panel cannot be removed, and a second Panel with the same name is
    // silently ignored (the first one keeps its content). So the Panel is
    // created once per BricsCAD session, around a ContentControl, and parked in
    // AppDomain data. Each plugin load puts its views into that ContentControl
    // and Dispose takes them out again, so an unloaded DevReload ALC is not kept
    // alive by BricsCAD. Only BricsCAD and WPF types go into AppDomain data: a
    // type from this assembly there would itself pin the ALC.
    internal sealed class ScriptPanel : IDisposable
    {
        private const string SlotKey = "Acd.Mcp.ScriptPanel";

        private readonly Panel _panel;
        private readonly ContentControl _host;
        private readonly SideTabHost _tabs = new();
        private readonly ScriptControl _scriptControl;
        private readonly BatchControl _batchControl;
        private readonly int _batchTabIndex;

        public ScriptPanel(
            AcadExecutor executor,
            ScriptSession session,
            ExecutionLog log,
            BatchExecutor batchExecutor,
            ScriptEditor scriptScriptEditor)
        {
            if (AppDomain.CurrentDomain.GetData(SlotKey) is not object[] slot)
            {
                var host = new ContentControl();
                // Name is also the tab label when STACKPANELTYPE = 0 (tabs).
                slot =
                [
                    new Panel("ACD-MCP", new DockingTemplate(DockSides.Right, "RDOCK", 40), host)
                        { Title = "ACD-MCP", Icon = GlyphIcon("") }, // Segoe MDL2 "Code"
                    host,
                ];
                AppDomain.CurrentDomain.SetData(SlotKey, slot);
            }
            _panel = (Panel)slot[0];
            _host = (ContentControl)slot[1];

            _scriptControl = new ScriptControl(executor, session, log, scriptScriptEditor);
            _batchControl = new BatchControl(batchExecutor);
            _tabs.Add("SCRIPT", _scriptControl);
            _batchTabIndex = _tabs.Add("BATCH", _batchControl);
            _host.Content = _tabs;
        }

        public BatchViewModel BatchViewModel => _batchControl.ViewModel;

        // Setting true brings the panel forward: opens it if the user closed
        // it (✕), and in a collapsed stack expands it (or selects its tab).
        public bool Visible
        {
            get => _panel.Visible;
            set
            {
                if (value) BringForward();
                else _panel.Visible = false;
            }
        }

        // For agent calls that change BATCH: the user must see the change.
        public void ShowBatchTab()
        {
            _tabs.Select(_batchTabIndex);
            BringForward();
        }

        // Plugin teardown. The panel stays where the user docked it; Dispose
        // empties it, and the next load fills it again.
        public void Close() { }

        // Visible = true alone does nothing on an open panel. Hide + show is
        // what -TOOLPANEL Show does: the panel stays in its stack and comes
        // to the front.
        private void BringForward()
        {
            if (_panel.Visible) _panel.Visible = false;
            _panel.Visible = true;
        }

        public void Dispose()
        {
            _host.Content = null;
            SafeBoundary.Run("ScriptPanel.Dispose(ScriptControl)", () => _scriptControl.Dispose());
            SafeBoundary.Run("ScriptPanel.Dispose(BatchControl)", () => _batchControl.Dispose());
        }

        // Panel icons must be bitmaps (a DrawingImage shows BricsCAD's "P"
        // placeholder), so the glyph is rendered once into one.
        private static System.Windows.Media.ImageSource GlyphIcon(string glyph)
        {
            const int px = 32;
            var text = new System.Windows.Media.FormattedText(glyph,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Windows.FlowDirection.LeftToRight,
                new System.Windows.Media.Typeface("Segoe MDL2 Assets"), px,
                System.Windows.Media.Brushes.White, 1.0);
            var visual = new System.Windows.Media.DrawingVisual();
            using (var dc = visual.RenderOpen())
                dc.DrawText(text, new System.Windows.Point(
                    (px - text.Width) / 2, (px - text.Height) / 2));
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
                px, px, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return bmp;
        }
    }
}
#endif
