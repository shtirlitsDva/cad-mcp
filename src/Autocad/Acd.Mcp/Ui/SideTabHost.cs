#if BRICSCAD
using System.Windows;
using System.Windows.Controls;

namespace Acd.Mcp.Ui
{
    // Vertical tabs down the left edge, the look of AutoCAD's PaletteSet tabs.
    // BricsCAD's Panel has no tabs of its own, so ScriptPanel puts this inside
    // one Panel. Styles are SideTab / SideTab.Strip in Theme.xaml.
    //
    // Pages stay in the tree and are only hidden, the way a PaletteSet switches
    // tabs: an editor keeps its caret, scroll and undo across tab switches.
    internal sealed class SideTabHost : Grid
    {
        private readonly StackPanel _strip = new();
        private readonly Grid _pages = new();

        public SideTabHost()
        {
            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/Acd.Mcp;component/Ui/Themes/Theme.xaml", UriKind.Relative),
            });

            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition());

            var strip = new Border { Child = _strip };
            strip.SetResourceReference(StyleProperty, "SideTab.Strip");
            SetColumn(_pages, 1);
            Children.Add(strip);
            Children.Add(_pages);
        }

        // Adds a tab and returns its index. The first tab added is selected.
        public int Add(string header, UIElement page)
        {
            // RadioButtons in one panel form one group: checking a tab
            // unchecks the others.
            var tab = new RadioButton { Content = header };
            tab.SetResourceReference(StyleProperty, "SideTab");
            page.Visibility = Visibility.Collapsed;
            tab.Checked += (_, _) => page.Visibility = Visibility.Visible;
            tab.Unchecked += (_, _) => page.Visibility = Visibility.Collapsed;

            _strip.Children.Add(tab);
            _pages.Children.Add(page);
            if (_strip.Children.Count == 1) tab.IsChecked = true;
            return _strip.Children.Count - 1;
        }

        public void Select(int index) => ((RadioButton)_strip.Children[index]).IsChecked = true;
    }
}
#endif
