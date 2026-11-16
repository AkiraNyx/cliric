using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LyricReplica.App;

internal sealed class SettingsWindow : Window
{
    private readonly ScrollViewer _body = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

    public SettingsWindow(Window owner, string title, double width, double height)
    {
        Owner = owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Title = title;
        Width = width;
        Height = height;
        MinWidth = 340;
        MinHeight = 280;
        Background = new SolidColorBrush(Color.FromRgb(24, 26, 35));
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        var layout = new DockPanel { Margin = new Thickness(24) };
        var heading = new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 18) };
        DockPanel.SetDock(heading, Dock.Top);
        layout.Children.Add(heading);
        var done = new Button { Content = "完成", IsDefault = true, MinWidth = 84, Padding = new Thickness(18, 6, 18, 6), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        done.Click += (_, _) => DialogResult = true;
        DockPanel.SetDock(done, Dock.Bottom);
        layout.Children.Add(done);
        layout.Children.Add(_body);
        Content = layout;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }

    public object? SettingsContent
    {
        get => _body.Content;
        set => _body.Content = value;
    }
}
