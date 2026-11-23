using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Shell;
using System.Windows.Interop;
using System.Windows.Media;
using Cliric.Core;
using Cliric.App.Reference;

namespace Cliric.App;

public sealed partial class MainWindow
{
    internal bool IsNativeWindowInsideWorkArea()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        const int extendedFrameBounds = 9;
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info) ||
            DwmGetWindowAttribute(hwnd, extendedFrameBounds, out var bounds, Marshal.SizeOf<ScreenRect>()) != 0)
            return false;
        return bounds.Left >= info.Work.Left - 1 && bounds.Top >= info.Work.Top - 1 &&
            bounds.Right <= info.Work.Right + 1 && bounds.Bottom <= info.Work.Bottom + 1;
    }

    internal int GetMaximizeCaptionHit()
    {
        if (_library?.MaximizeWindowButton is not { IsVisible: true } button)
            return 0;
        return GetNativeHit(button.PointToScreen(new Point(button.ActualWidth / 2, button.ActualHeight / 2)));
    }

    internal int GetNativeHit(Point screen)
    {
        const int nonClientHitTest = 0x0084;
        int position = ((int)screen.X & 0xffff) | ((int)screen.Y << 16);
        return SendMessage(new WindowInteropHelper(this).Handle, nonClientHitTest, IntPtr.Zero, (IntPtr)position).ToInt32();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out ScreenRect value, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    private void CheckLyricPlaybackHitTargets()
    {
        var buttons = _reference.Elements.OfType<ImageButton>().Where(button => button.IsVisible && InputMap.GetClick(button).Length > 0);
        var targets = buttons.Cast<FrameworkElement>().Append((_portrait ? _seekVertical : _seek).BorderCover).ToArray();
        if (targets.Length < 6)
            throw new InvalidOperationException("The lyrics playback controls must be visible in the test layout.");
        foreach (var control in targets)
        {
            var center = new Point(control.ActualWidth / 2, control.ActualHeight / 2);
            var hit = InputHitTest(control.TranslatePoint(center, this));
            int nativeHit = GetNativeHit(control.PointToScreen(center));
            if (!control.IsEnabled || hit is not Visual visual ||
                (!ReferenceEquals(control, visual) && !control.IsAncestorOf(visual)) || nativeHit != 1)
                throw new InvalidOperationException($"Playback target {control.Name}/{InputMap.GetClick(control)} hit {hit?.GetType().Name}, native={nativeHit}, enabled={control.IsEnabled}.");
        }
    }

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
            ExecuteMagic(MagicAction.Maximize);
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
            if (!_fullscreen || Topmost || Math.Abs(bounds.contentWidth - bounds.screenWidth) > 2 || Math.Abs(bounds.contentHeight - bounds.screenHeight) > 2)
                throw new InvalidOperationException("Maximized content/pinning differs from the current primary monitor policy: " + JsonSerializer.Serialize(bounds));
            _options.Background = BackgroundMode.Modern;
            _options.AppleMusicLyrics.EnableIndependentBackgroundRenderer = true;
            UpdateWindowChrome();
            await Task.Delay(200);
            if (WindowState != WindowState.Maximized || ResizeMode != ResizeMode.NoResize)
                throw new InvalidOperationException("Independent maximized mode did not enter NoResize.");
            ExecuteMagic(MagicAction.Restore);
            await Task.Delay(200);
            if (_fullscreen || !Topmost || ResizeMode != ResizeMode.CanResize || WindowChrome.GetWindowChrome(this) != (_hostedByPlayer ? _libraryChrome : _lyricsChrome) || _root.Margin != new Thickness(0) || Math.Abs(ActualWidth - width) > 1 || Math.Abs(ActualHeight - height) > 1)
                throw new InvalidOperationException("Restoring the native window did not recover its size, pinning and resize border.");
            if (_hostedByPlayer)
            {
                SystemCommands.MaximizeWindow(this);
                await Task.Delay(200);
                if (!_fullscreen || WindowState != WindowState.Maximized || Math.Abs(_root.ActualHeight - bounds.screenHeight) > 2)
                    throw new InvalidOperationException("The system maximize command must enter lyrics fullscreen.");
                SystemCommands.RestoreWindow(this);
                await Task.Delay(200);
                if (_fullscreen || WindowState != WindowState.Normal)
                    throw new InvalidOperationException("The system restore command must exit lyrics fullscreen.");
            }
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
        finally { _options.Background = background; _options.AppleMusicLyrics.EnableIndependentBackgroundRenderer = independent; _options.Topmost = pinned; if (_fullscreen) ToggleFullscreen(); WindowState = WindowState.Normal; UpdateWindowChrome(); Width = width; Height = height; }
    }
}
