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
using Cliric.Core;
using Cliric.App.Reference;

namespace Cliric.App;

public sealed partial class MainWindow
{
    private static readonly DependencyProperty LyricsProgressProperty = DependencyProperty.Register(
        nameof(LyricsProgress), typeof(double), typeof(MainWindow),
        new PropertyMetadata(0d, (window, change) => ((MainWindow)window).SetPageBlend((double)change.NewValue)));
    private static readonly BitmapCache PageFadeCache = MotionTokens.Freeze(new BitmapCache());
    private const double LibraryShadeOpacity = .12;
    private readonly Grid _shell = new();
    private readonly Border _backdropNeutral = new() { IsHitTestVisible = false };
    private readonly Border _backdropShade = new() { Background = Brushes.Black, IsHitTestVisible = false };
    private readonly WindowChrome _libraryChrome = new() { UseAeroCaptionButtons = false };
    private LibraryView? _library;
    private readonly Border _libraryTransition = new() { Background = Brushes.Transparent },
        _lyricsTransition = new() { Background = Brushes.Transparent, IsHitTestVisible = false };
    private PageFadeEffect _libraryFade = null!, _lyricsFade = null!;
    private Viewbox? _shellLegacyBackdrop;
    private bool _lyricsShown, _lyricsEntering, _stoppingForClose;
    private double _lastVolumeSent;

    internal bool LyricsShown => _lyricsShown;
    internal bool CanPresentIndependentBackground => WindowState switch
    {
        WindowState.Maximized => !_hostedByPlayer || _fullscreen,
        WindowState.Normal => _hostedByPlayer && _options.Background == BackgroundMode.ReferenceAppleMusic &&
            (_lyricsShown ? LyricsProgress >= 1 : LyricsProgress > 0),
        _ => false
    };
    internal double LyricsProgress => (double)GetValue(LyricsProgressProperty);

    private void BuildShell()
    {
        var menu = ContextMenu;
        ContextMenu = null;
        _root.ContextMenu = menu;
        Content = null;
        _library = new LibraryView(this, _args.Contains("--self-test"));
        _root.Background = Brushes.Transparent;
        _root.Visibility = Visibility.Hidden;
        _shell.Background = Brushes.Black;
        _shell.ClipToBounds = true;
        _root.Children.Remove(_background);
        _shellLegacyBackdrop = _reference.Get<Viewbox>("ViewboxBackground");
        _stage.Children.Remove(_shellLegacyBackdrop);
        // The relocated backdrop still binds to the artwork and blur names in the lyric page.
        NameScope.SetNameScope(_shell, NameScope.GetNameScope(_reference.View));
        _shellLegacyBackdrop.IsHitTestVisible = false;
        _backdropNeutral.Background = (Brush)_library.FindResource("Shell.Background");
        _shell.Children.Add(_background);
        _shell.Children.Add(_shellLegacyBackdrop);
        _shell.Children.Add(_backdropNeutral);
        _shell.Children.Add(_backdropShade);
        _libraryTransition.Child = _library;
        _lyricsTransition.Child = _root;
        _libraryFade = new PageFadeEffect();
        _lyricsFade = new PageFadeEffect();
        _shell.Children.Add(_libraryTransition);
        _shell.Children.Add(_lyricsTransition);
        SetPageBlend(0);
        Content = _shell;
        Title = "Cliric";
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
        const int systemCommand = 0x0112;
        const int maximize = 0xF030, restore = 0xF120;
        const int ncHitTest = 0x0084;
        const int ncMouseMove = 0x00A0;
        const int ncLeftButtonDown = 0x00A1;
        const int ncMouseLeave = 0x02A2;
        const int mouseMove = 0x0200;
        const int maxButton = 9;
        if (_lyricsShown && !_updatingWindowChrome && message == systemCommand)
        {
            long command = wParam.ToInt64() & 0xFFF0;
            if (command == maximize && !_fullscreen ||
                command == restore && _fullscreen && WindowState == WindowState.Maximized)
            {
                handled = true;
                ToggleFullscreen();
                return IntPtr.Zero;
            }
        }
        if (_lyricsShown || _fullscreen || _library is not { } library)
            return ConstrainMaximizedLibrary(hwnd, message, wParam, lParam, ref handled);

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
        if (_fullscreen || message != ncCalcSize || !IsZoomed(hwnd))
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
        if (_library is null || _disposed || !CanOpenLyrics)
            return;
        if (_lyricsShown)
            return;
        _lyricsShown = true;
        UpdateWindowChrome();
        _lyricsEntering = false;
        _root.Visibility = Visibility.Visible;
        _lyricsTransition.IsHitTestVisible = true;
        _libraryTransition.IsHitTestVisible = false;
        _library.Pause(true);
        UpdateLyricsVisibility();
        if (!_lyricsLoaded)
        {
            _lyricsLoaded = true;
            _magicController.BeginTutorial(_wall.Elapsed.TotalMilliseconds);
        }
        _root.UpdateLayout();
        OnRendering(null, EventArgs.Empty);
        _lyricsEntering = !ShellTransitions.GetReducedMotion(_library);
        AnimatePageBlend(animate: true);
        Focus();
    }

    internal void ShowLibrary() => ReturnToLibrary(animate: true);

    private void ReturnToLibrary(bool animate)
    {
        if (_library is null || _disposed || !_lyricsShown)
            return;
        _lyricsShown = false;
        _lyricsEntering = false;
        _queue.Visibility = Visibility.Collapsed;
        foreach (var popup in new[] { _root.ContextMenu, _reference.Get<ContextMenu>("MenuThreeDot"), _reference.Get<ContextMenu>("MenuArtistNameClick") })
            if (popup is not null)
                popup.IsOpen = false;
        _lyricsTransition.IsHitTestVisible = false;
        _scroll.StopWheelAnimation();
        UpdateLyricsVisibility();
        UpdateWindowChrome();
        _library.Visibility = Visibility.Visible;
        _libraryTransition.IsHitTestVisible = true;
        AnimatePageBlend(animate);
    }

    private void AnimatePageBlend(bool animate)
    {
        bool showLyrics = _lyricsShown;
        double target = showLyrics ? 1 : 0;
        var timing = MotionTokens.LyricsPage with
        {
            Milliseconds = (int)Math.Ceiling(MotionTokens.LyricsPage.Milliseconds * Math.Abs(target - LyricsProgress))
        };
        bool immediate = !animate || ShellTransitions.GetReducedMotion(_library!);
        if (!immediate && timing.Milliseconds > 0)
        {
            // Realize both foreground caches before the incoming layer gains opacity.
            PrepareFade(_libraryTransition, _libraryFade);
            PrepareFade(_lyricsTransition, _lyricsFade);
            SetPageBlend(LyricsProgress);
        }
        ShellTransitions.To(this, LyricsProgressProperty, target, timing,
            immediate: immediate, completed: () =>
            {
                _libraryTransition.Effect = _lyricsTransition.Effect = null;
                _libraryTransition.CacheMode = _lyricsTransition.CacheMode = null;
                SetPageBlend(target);
                _lyricsEntering = false;
                if (_disposed)
                    return;
                if (showLyrics)
                    _library!.Visibility = Visibility.Hidden;
                else
                {
                    _root.Visibility = Visibility.Hidden;
                    _library!.Pause(false);
                    // Complete the blend in one viewport before restoring explicit fullscreen.
                    if (_fullscreen)
                        ToggleFullscreen();
                }
            }, beginOnNextTick: true);

        static void PrepareFade(Border layer, PageFadeEffect effect)
        {
            layer.CacheMode = PageFadeCache;
            layer.Effect = effect;
            layer.Opacity = 1;
        }
    }

    private void SetPageBlend(double progress)
    {
        double tint = 1 - MotionTokens.Move.Ease(progress);
        _backdropNeutral.Opacity = .58 * tint;
        _backdropShade.Opacity = LibraryShadeOpacity * tint;
        double delay = (double)MotionTokens.LyricsPageStaggerMs / MotionTokens.LyricsPage.Milliseconds;
        bool warm = progress == (_lyricsShown ? 0 : 1);
        FadeLayer(_libraryTransition, 1 - MotionTokens.Move.Ease(Math.Clamp(progress / (1 - delay), 0, 1)), warm);
        FadeLayer(_lyricsTransition, MotionTokens.Move.Ease(Math.Clamp((progress - delay) / (1 - delay), 0, 1)), warm);

        static void FadeLayer(Border layer, double opacity, bool warm)
        {
            if (layer.Effect is PageFadeEffect effect)
            {
                bool draw = warm || opacity > 1d / 255;
                layer.Opacity = draw ? 1 : 0;
                if (draw)
                    effect.SetPose(opacity, VisualTreeHelper.GetDpi(layer).PixelsPerDip);
            }
            else
                layer.Opacity = opacity;
        }
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
        var dpi = VisualTreeHelper.GetDpi(_shell);
        int width = Math.Max(1, (int)Math.Ceiling(_shell.ActualWidth * dpi.DpiScaleX));
        int height = Math.Max(1, (int)Math.Ceiling(_shell.ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(_shell);
        File.WriteAllBytes(path, Artwork.Encode(bitmap));
    }
}
