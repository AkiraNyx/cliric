using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using LyricReplica.Core;
using LyricReplica.App.Reference;

namespace LyricReplica.App;

/// <summary>
/// One window for the whole app: the library sits underneath and the lyric page slides up over it.
/// The lyric page keeps its original root so its own checks and captures are unchanged.
/// </summary>
public sealed partial class MainWindow
{
    private readonly Grid _shell = new();
    private readonly TranslateTransform _lyricsSlide = new();
    private readonly WindowChrome _libraryChrome = new() { UseAeroCaptionButtons = false };
    private LibraryView? _library;
    private bool _lyricsShown, _lyricsEntering, _stoppingForClose;
    private int _lyricsTransition;
    private double _lastVolumeSent;

    internal bool LyricsShown => _lyricsShown;
    internal bool ReduceMotion => _options.ReduceMotion;
    internal double LyricsOffset => _lyricsSlide.Y;

    private void BuildShell()
    {
        var menu = ContextMenu;
        ContextMenu = null;
        _root.ContextMenu = menu;
        Content = null;
        _library = new LibraryView(this, _args.Contains("--self-test"));
        _root.RenderTransform = _lyricsSlide;
        _root.Visibility = Visibility.Hidden;
        _shell.Background = Brushes.Black;
        _shell.ClipToBounds = true;
        _shell.Children.Add(_library);
        _shell.Children.Add(_root);
        Content = _shell;
        Title = "LyricReplica";
        MinWidth = 500;
        Closing += StopPlaybackBeforeClose;
        UpdateWindowChrome();
        SourceInitialized += (_, _) =>
        {
            const int immersiveDarkMode = 20;
            var handle = new WindowInteropHelper(this).Handle;
            int dark = SystemParameters.HighContrast ? 0 : 1;
            DwmSetWindowAttribute(handle, immersiveDarkMode, ref dark, sizeof(int));
            // HwndSource calls the latest hook first, before WindowChrome's client calculation.
            HwndSource.FromHwnd(handle).AddHook(HandleLibraryFrame);
        };
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private IntPtr HandleLibraryFrame(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int ncHitTest = 0x0084;
        const int ncMouseMove = 0x00A0;
        const int ncLeftButtonDown = 0x00A1;
        const int ncMouseLeave = 0x02A2;
        const int mouseMove = 0x0200;
        const int maxButton = 9;
        if (_lyricsShown || _library is not { } library)
            return IntPtr.Zero;

        if (message == ncHitTest && library.MaximizeWindowButton is { IsLoaded: true, IsVisible: true, IsEnabled: true } button)
        {
            long packed = lParam.ToInt64();
            var point = new Point(unchecked((short)packed), unchecked((short)(packed >> 16)));
            if (new Rect(button.RenderSize).Contains(button.PointFromScreen(point)))
            {
                handled = true;
                return (IntPtr)maxButton;
            }
        }
        if (message == ncLeftButtonDown && wParam == (IntPtr)maxButton && library.BeginMaximizePress())
        {
            handled = true;
            return IntPtr.Zero;
        }
        if (message == ncMouseMove && wParam == (IntPtr)maxButton)
        {
            if (ShellTransitions.GetHoverOpacity(library.MaximizeHover) == 0)
            {
                const uint leaveNonClient = 0x00000012;
                var tracking = new MouseTracking { Size = Marshal.SizeOf<MouseTracking>(), Flags = leaveNonClient, Window = hwnd };
                TrackMouseEvent(ref tracking);
                ShellTransitions.SetHoverOpacity(library.MaximizeHover, 1);
            }
        }
        else if ((message is ncMouseMove or ncMouseLeave or mouseMove) && !library.MaximizeWindowButton.IsMouseCaptured)
            ShellTransitions.SetHoverOpacity(library.MaximizeHover, 0);

        return ConstrainMaximizedLibrary(hwnd, message, wParam, lParam, ref handled);
    }

    private IntPtr ConstrainMaximizedLibrary(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int ncCalcSize = 0x0083;
        const int redraw = 0x0300;
        const uint nearestMonitor = 2;
        if (_lyricsShown || message != ncCalcSize || !IsZoomed(hwnd))
            return IntPtr.Zero;

        var client = Marshal.PtrToStructure<ScreenRect>(lParam);
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromRect(ref client, nearestMonitor), ref monitor))
            return IntPtr.Zero;

        // Keep the client origin for native caption hit testing, and fit the WPF content to the work area.
        var dpi = VisualTreeHelper.GetDpi(this);
        _shell.Margin = new Thickness(
            Math.Max(0, monitor.Work.Left - client.Left) / dpi.DpiScaleX,
            Math.Max(0, monitor.Work.Top - client.Top) / dpi.DpiScaleY,
            Math.Max(0, client.Right - monitor.Work.Right) / dpi.DpiScaleX,
            0);
        client.Bottom = Math.Min(client.Bottom, monitor.Work.Bottom);
        Marshal.StructureToPtr(client, lParam, false);
        handled = true;
        return wParam == IntPtr.Zero ? IntPtr.Zero : (IntPtr)redraw;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public ScreenRect Monitor, Work; public uint Flags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseTracking { public int Size; public uint Flags; public IntPtr Window; public uint HoverTime; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TrackMouseEvent(ref MouseTracking tracking);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref ScreenRect rectangle, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    internal void ShowLyrics()
    {
        if (_library is null || _disposed)
            return;
        if (_lyricsShown)
            return;
        int transition = ++_lyricsTransition;
        bool wasHidden = _root.Visibility != Visibility.Visible;
        _lyricsShown = true;
        UpdateWindowChrome();
        if (wasHidden)
        {
            _shell.UpdateLayout();
            ShellMotion.To(_lyricsSlide, TranslateTransform.YProperty, Math.Max(1, _shell.ActualHeight), 0);
        }
        _lyricsEntering = false;
        _root.Visibility = Visibility.Visible;
        _root.IsHitTestVisible = true;
        _library.IsHitTestVisible = false;
        _library.Pause(true);
        UpdateLyricsVisibility();
        if (!_lyricsLoaded)
        {
            _lyricsLoaded = true;
            _magicController.BeginTutorial(_wall.Elapsed.TotalMilliseconds);
        }
        _root.UpdateLayout();
        OnRendering(null, EventArgs.Empty);
        _lyricsEntering = !ReduceMotion;
        ShellMotion.To(_lyricsSlide, TranslateTransform.YProperty, 0, 360, ShellMotion.Out, completed: () =>
        {
            if (transition != _lyricsTransition || _disposed)
                return;
            _lyricsEntering = false;
            _library.Visibility = Visibility.Hidden;
        });
        Focus();
    }

    internal void ShowLibrary() => ReturnToLibrary(animate: true);

    private void ReturnToLibrary(bool animate)
    {
        if (_library is null || _disposed || !_lyricsShown)
            return;
        int transition = ++_lyricsTransition;
        _lyricsShown = false;
        _lyricsEntering = false;
        _queue.Visibility = Visibility.Collapsed;
        foreach (var popup in new[] { _root.ContextMenu, _reference.Get<ContextMenu>("MenuThreeDot"), _reference.Get<ContextMenu>("MenuArtistNameClick") })
            if (popup is not null)
                popup.IsOpen = false;
        _root.IsHitTestVisible = false;
        _scroll.StopWheelAnimation();
        if (_fullscreen)
            ToggleFullscreen();
        UpdateLyricsVisibility();
        UpdateIndependentPresentation(false);
        UpdateWindowChrome();
        _library.Visibility = Visibility.Visible;
        _library.IsHitTestVisible = true;
        _library.Pause(false);
        _shell.UpdateLayout();
        double height = Math.Max(1, _shell.ActualHeight);
        ShellMotion.To(_lyricsSlide, TranslateTransform.YProperty, height, animate ? 260 : 0, ShellMotion.Out, completed: () =>
        {
            if (transition == _lyricsTransition)
                _root.Visibility = Visibility.Hidden;
        });
    }

    internal void OpenLyricsSettings()
    {
        if (_library is null)
            return;
        ReturnToLibrary(animate: false);
        _library.ShowLyricsSettings();
    }

    internal SettingsPanel CreateLyricsSettings(Action reload) => new(_options, ApplyLyricsSettings, () => Guard(() =>
    {
        _options = new() { Volume = _options.Volume, RealtimeSeek = _options.RealtimeSeek, NetEaseQuality = _options.NetEaseQuality };
        ApplyLyricsSettings(true);
        reload();
        Notify("已恢复默认歌词设置");
    }), reload);

    private void ApplyLyricsSettings(bool background) => Guard(() =>
    {
        _options.Normalize();
        ReferenceMenus.UpdateTheme(_options.LightTheme);
        Topmost = _options.Topmost;
        _audio.Volume = _options.Volume;
        UpdateWindowChrome();
        UpdateSleepRequest();
        if (background)
        {
            UpdateIndependentPresentation(false);
            _ = _background.SetModeAsync(_options.Background, _options, true);
        }
        App.SaveOptions(_options);
        _rebuildPending = true;
        _resetWordRuns = true;
        _anchor = -2;
    });

    internal void SetVolume(double volume, bool persist)
    {
        _options.Volume = Math.Clamp(volume, 0, 1);
        _audio.Volume = _options.Volume;
        double now = _wall.Elapsed.TotalSeconds;
        if (_netEaseActive && (persist || now - _lastVolumeSent > .08))
        {
            _lastVolumeSent = now;
            _ = ControlNetEaseAsync("volume", _options.Volume * 100);
        }
        if (persist)
            Guard(() => App.SaveOptions(_options));
    }

    private bool HandleLibraryKey(KeyEventArgs e)
    {
        if (e.Key != Key.Space || e.OriginalSource is TextBox or System.Windows.Controls.Primitives.ButtonBase or ListBoxItem or ComboBox or ComboBoxItem or Slider)
            return false;
        TogglePlay();
        return true;
    }

    private async void StopPlaybackBeforeClose(object? sender, CancelEventArgs e)
    {
        if (!_netEaseActive)
            return;
        e.Cancel = true;
        if (_stoppingForClose)
            return;
        _stoppingForClose = true;
        try
        {
            await StopNetEaseAsync();
            Close();
        }
        catch (Exception exception)
        {
            Notify(exception.Message);
            App.Log(exception.ToString());
        }
        finally { _stoppingForClose = false; }
    }

    internal void CaptureShell(string path)
    {
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)_shell.ActualWidth), Math.Max(1, (int)_shell.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(_shell);
        File.WriteAllBytes(path, Artwork.Encode(bitmap));
    }
}
