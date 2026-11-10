using System.Diagnostics;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LyricReplica.App.Reference;

internal sealed class IsolationBackground : Grid, IDisposable
{
    private readonly Image _image = new() { Stretch = Stretch.Fill, SnapsToDevicePixels = true, UseLayoutRounding = true };
    private readonly double _scale;
    private readonly float _dimming;
    private readonly long _interval;
    private readonly bool _static;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly int _timeOffset = Random.Shared.Next(100);
    private IsolationGraphics? _graphics;
    private IsolationSurfaceBridge? _bridge;
    private Vector3[] _colors = new Vector3[4], _from = new Vector3[4], _to = new Vector3[4];
    private bool _paletteReady, _transition, _dirty = true, _subscribed, _disposed;
    private long _next;
    private (int Width, int Height) _size;
    private double _dpiX, _dpiY;
    public event Action<Exception>? Fault;
    public IsolationBackground(int? frameRate, double renderScale, float dimming)
    {
        _scale = double.IsFinite(renderScale) && renderScale > 0 ? Math.Clamp(renderScale, .125, 2) : 1;
        _dimming = Math.Clamp(dimming, 0, 1);
        _static = frameRate == 0;
        int? rate = frameRate < 0 ? 30 : frameRate;
        _interval = rate > 0 ? Math.Max(1, Stopwatch.Frequency / rate.Value) : 0;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
        Children.Add(_image);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => _dirty = true;
    }
    public void UpdateColors(IReadOnlyList<Vector3> colors)
    {
        Dispatcher.VerifyAccess();
        if (colors.Count != 4)
            throw new ArgumentException("Isolation requires four palette colors.", nameof(colors));
        if (colors.Any(c => !float.IsFinite(c.X) || !float.IsFinite(c.Y) || !float.IsFinite(c.Z)))
            throw new ArgumentException("Palette contains a non-finite color.", nameof(colors));
        if (!_paletteReady)
        {
            _colors = colors.ToArray();
            _paletteReady = true;
        }
        else
        {
            _from = _colors.ToArray();
            _to = colors.ToArray();
            _transition = true;
            _clock.Restart();
        }
        _dirty = true;
    }
    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_disposed || _subscribed)
            return;
        _subscribed = true;
        CompositionTarget.Rendering += OnRendering;
        _dirty = true;
    }
    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        StopRendering();
        ReleaseDevice();
    }
    private void OnRendering(object? sender, EventArgs args)
    {
        if (_disposed || !IsLoaded || !IsVisible || !_paletteReady || ActualWidth <= 0 || ActualHeight <= 0)
            return;
        long now = Stopwatch.GetTimestamp();
        if (!_dirty && (_static || _interval > 0 && now < _next))
            return;
        try
        {
            _graphics ??= new IsolationGraphics();
            _bridge ??= new IsolationSurfaceBridge();
            var dpi = VisualTreeHelper.GetDpi(this);
            var size = (Width: Math.Max(1, (int)Math.Round(ActualWidth * dpi.DpiScaleX * _scale, MidpointRounding.AwayFromZero)), Height: Math.Max(1, (int)Math.Round(ActualHeight * dpi.DpiScaleY * _scale, MidpointRounding.AwayFromZero)));
            double dpiX = dpi.PixelsPerInchX * _scale, dpiY = dpi.PixelsPerInchY * _scale;
            if (size != _size || Math.Abs(dpiX - _dpiX) > .01 || Math.Abs(dpiY - _dpiY) > .01)
            {
                _graphics.Resize(size.Width, size.Height);
                _bridge.Attach(_graphics.Texture, dpiX, dpiY);
                _image.Source = _bridge.Source;
                _size = size;
                _dpiX = dpiX;
                _dpiY = dpiY;
            }
            if (_transition)
            {
                float progress = Math.Clamp((float)(_clock.Elapsed.TotalSeconds / 2), 0, 1);
                if (progress >= 1)
                    _transition = false;
                float eased = progress < .5 ? 4 * progress * progress * progress : 1 - MathF.Pow(-2 * progress + 2, 3) / 2;
                for (int i = 0; i < 4; i++)
                    _colors[i] = Vector3.Lerp(_from[i], _to[i], eased);
            }
            _bridge.Draw(() => _graphics.Draw((float)(_clock.Elapsed.TotalSeconds + _timeOffset), _colors, _dimming));
            _dirty = false;
            _next = now + _interval;
        }
        catch (Exception exception) { StopRendering(); ReleaseDevice(); Fault?.Invoke(exception); }
    }
    private void StopRendering()
    {
        if (!_subscribed)
            return;
        _subscribed = false;
        CompositionTarget.Rendering -= OnRendering;
    }
    private void ReleaseDevice()
    {
        _image.Source = null;
        try
        {
            _bridge?.Dispose();
        }
        finally { _graphics?.Dispose(); _bridge = null; _graphics = null; _size = default; _dirty = true; }
    }
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        StopRendering();
        ReleaseDevice();
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
    }
}
