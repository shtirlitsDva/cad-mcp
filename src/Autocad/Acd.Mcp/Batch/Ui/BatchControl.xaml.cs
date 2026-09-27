using System;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Xml;
using Acd.Mcp.Batch.Runtime;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace Acd.Mcp.Batch.Ui
{
    // Mirrors the REPL palette's pattern: AvalonEdit's Text isn't a DP, so
    // we two-way sync it to the VM's CurrentScript with a re-entrancy
    // guard, and we load the same dark XSHD definition.
    public partial class BatchControl : UserControl, IDisposable
    {
        private const string DarkSyntaxResourceName = "Acd.Mcp.Ui.Themes.CSharp-Dark.xshd";

        private readonly BatchViewModel _vm;
        private bool _suppressSync;

        public BatchControl(BatchExecutor executor)
        {
            InitializeComponent();
            _vm = new BatchViewModel(executor);
            DataContext = _vm;

            ApplyDarkSyntax();
            Editor.Text = _vm.CurrentScript;
            Editor.TextChanged += OnEditorTextChanged;
            _vm.PropertyChanged += OnVmPropertyChanged;
        }

        // Expose the VM so the palette can publish it as the IBatchUiState
        // implementation the pipe handler reads from.
        public BatchViewModel ViewModel => _vm;

        private void ApplyDarkSyntax() => SafeBoundary.Run("BatchControl.ApplyDarkSyntax", () =>
        {
            var asm = typeof(BatchControl).Assembly;
            using var stream = asm.GetManifestResourceStream(DarkSyntaxResourceName);
            if (stream is null) return;
            using var reader = XmlReader.Create(stream);
            Editor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        });

        private void OnEditorTextChanged(object? sender, EventArgs e) =>
            SafeBoundary.Run("BatchControl.OnEditorTextChanged", () =>
            {
                if (_suppressSync) return;
                _vm.CurrentScript = Editor.Text;
            });

        // The file list (MatchPopup) is open while the mouse is on the box or
        // on the list. Leaving starts a short delay before the close, so the
        // mouse can cross from the box to the list.
        private static readonly TimeSpan MatchPopupCloseDelay = TimeSpan.FromMilliseconds(150);
        private DispatcherTimer? _matchPopupCloseTimer;

        private void OnMatchAreaMouseEnter(object sender, MouseEventArgs e) =>
            SafeBoundary.Run("BatchControl.OnMatchAreaMouseEnter", () =>
            {
                _matchPopupCloseTimer?.Stop();
                if (_vm.HasFileLists) MatchPopup.IsOpen = true;
            });

        private void OnMatchAreaMouseLeave(object sender, MouseEventArgs e) =>
            SafeBoundary.Run("BatchControl.OnMatchAreaMouseLeave", () =>
            {
                _matchPopupCloseTimer ??= CreateMatchPopupCloseTimer();
                _matchPopupCloseTimer.Stop();
                _matchPopupCloseTimer.Start();
            });

        private DispatcherTimer CreateMatchPopupCloseTimer()
        {
            var timer = new DispatcherTimer { Interval = MatchPopupCloseDelay };
            timer.Tick += (_, _) => SafeBoundary.Run("BatchControl.MatchPopupClose", () =>
            {
                timer.Stop();
                var popupContent = MatchPopup.Child;
                if (!MatchBadge.IsMouseOver && !(popupContent?.IsMouseOver ?? false))
                    MatchPopup.IsOpen = false;
            });
            return timer;
        }

        // On the list itself, its ScrollViewer takes the wheel. On the box,
        // the wheel scrolls the list here. One notch (Delta 120) scrolls 40 px.
        private void OnMatchBadgeMouseWheel(object sender, MouseWheelEventArgs e) =>
            SafeBoundary.Run("BatchControl.OnMatchBadgeMouseWheel", () =>
            {
                if (!MatchPopup.IsOpen) return;
                MatchScroll.ScrollToVerticalOffset(MatchScroll.VerticalOffset - e.Delta / 3.0);
                e.Handled = true;
            });

        private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
            SafeBoundary.Run("BatchControl.OnVmPropertyChanged", () =>
            {
                // A new scan, or an error, while the list is open: nothing left to show.
                if (e.PropertyName == nameof(BatchViewModel.HasFileLists) && !_vm.HasFileLists)
                    MatchPopup.IsOpen = false;
                if (e.PropertyName != nameof(BatchViewModel.CurrentScript)) return;
                if (Editor.Text == _vm.CurrentScript) return;
                _suppressSync = true;
                try { Editor.Text = _vm.CurrentScript; }
                finally { _suppressSync = false; }
            });

        public void Dispose() => SafeBoundary.Run("BatchControl.Dispose", () =>
        {
            Editor.TextChanged -= OnEditorTextChanged;
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _matchPopupCloseTimer?.Stop();
            MatchPopup.IsOpen = false;
            _vm.Dispose();
        });
    }
}
