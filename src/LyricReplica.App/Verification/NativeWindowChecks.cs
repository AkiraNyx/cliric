using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Shell;
using LyricReplica.Core;

namespace LyricReplica.App;

public sealed partial class MainWindow
{
    private async Task CheckNativeWindowAsync(string folder)
    {
        double width = Width, height = Height;
        var background = _options.Background;
        bool independent = _options.AppleMusicLyrics.EnableIndependentBackgroundRenderer;
        bool pinned = _options.Topmost;
        string report = Path.Combine(folder, "native-window.json");
        File.Delete(report);
        try
        {
            _options.Topmost = true;
            Topmost = true;
            WindowState = WindowState.Maximized;
            await Task.Delay(200);
            var bounds = new
            {
                windowWidth = ActualWidth,
                windowHeight = ActualHeight,
                contentWidth = _root.ActualWidth,
                contentHeight = _root.ActualHeight,
                screenWidth = SystemParameters.PrimaryScreenWidth,
                screenHeight = SystemParameters.PrimaryScreenHeight,
                workHeight = SystemParameters.WorkArea.Height
            };
            File.WriteAllText(report, JsonSerializer.Serialize(new
            {
                passed = false,
                assemblySha256 = App.AssemblySha256,
                bounds
            }, LyricsFile.JsonOptions));
            if (Topmost || Math.Abs(bounds.contentWidth - bounds.screenWidth) > 2 || Math.Abs(bounds.contentHeight - bounds.screenHeight) > 2)
                throw new InvalidOperationException("Maximized content/pinning differs from the current primary monitor policy: " + JsonSerializer.Serialize(bounds));
            _options.Background = BackgroundMode.Modern;
            _options.AppleMusicLyrics.EnableIndependentBackgroundRenderer = true;
            UpdateWindowChrome();
            await Task.Delay(200);
            if (WindowState != WindowState.Maximized || ResizeMode != ResizeMode.NoResize)
                throw new InvalidOperationException("Independent maximized mode did not enter NoResize.");
            WindowState = WindowState.Normal;
            await Task.Delay(200);
            if (!Topmost || ResizeMode != ResizeMode.CanResize || WindowChrome.GetWindowChrome(this).ResizeBorderThickness != new Thickness(5) || _root.Margin != new Thickness(0) || Math.Abs(ActualWidth - width) > 1 || Math.Abs(ActualHeight - height) > 1)
                throw new InvalidOperationException("Restoring the native window did not recover its size, pinning and resize border.");
            File.WriteAllText(report, JsonSerializer.Serialize(new
            {
                passed = true,
                assemblySha256 = App.AssemblySha256,
                bounds,
                restoredWidth = ActualWidth,
                restoredHeight = ActualHeight,
                independentMaximizedNoResize = true,
                windowShown = true,
                desktopInputSent = false,
                scope = "Native primary-monitor client bounds and resize-mode transitions; physical multi-monitor/taskbar comparison remains separate."
            }, LyricsFile.JsonOptions));
        }
        finally { _options.Background = background; _options.AppleMusicLyrics.EnableIndependentBackgroundRenderer = independent; _options.Topmost = pinned; WindowState = WindowState.Normal; UpdateWindowChrome(); Width = width; Height = height; }
    }
}
