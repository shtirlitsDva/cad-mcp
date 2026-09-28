using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Acd.Mcp.Ui
{
    // A Button inside a ComboBox row never gets its Click: while the dropdown
    // is open the ComboBox takes the mouse capture back, and
    // ComboBoxItem.OnMouseLeftButtonUp turns the mouse-up into a row
    // selection (ComboBox.NotifyComboBoxItemMouseUp). With this set, the
    // button runs its Command on the preview mouse-up, which comes first,
    // and marks the event handled, so the ComboBox does not select the row.
    // Set it from a Style: Theme.xaml, Button.ComboBoxRow.
    public static class ComboBoxRowButton
    {
        public static readonly DependencyProperty ClicksInsideComboBoxProperty =
            DependencyProperty.RegisterAttached(
                "ClicksInsideComboBox", typeof(bool), typeof(ComboBoxRowButton),
                new PropertyMetadata(false, OnClicksInsideComboBoxChanged));

        public static bool GetClicksInsideComboBox(DependencyObject d) => (bool)d.GetValue(ClicksInsideComboBoxProperty);
        public static void SetClicksInsideComboBox(DependencyObject d, bool value) => d.SetValue(ClicksInsideComboBoxProperty, value);

        private static void OnClicksInsideComboBoxChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ButtonBase button) return;
            button.PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;
            if ((bool)e.NewValue) button.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
        }

        private static void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
            SafeBoundary.Run("ComboBoxRowButton.Click", () =>
            {
                var button = (ButtonBase)sender;
                if (!button.IsMouseOver) return; // released outside the button: no click
                e.Handled = true;
                var command = button.Command;
                var parameter = button.CommandParameter;
                if (command?.CanExecute(parameter) == true) command.Execute(parameter);
            });
    }
}
