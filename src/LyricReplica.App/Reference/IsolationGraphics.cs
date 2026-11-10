using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using D2DPixelFormat = Vortice.DCommon.PixelFormat;
using D2DAlphaMode = Vortice.DCommon.AlphaMode;

namespace LyricReplica.App.Reference;

internal sealed class IsolationGraphics : IDisposable
{
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _immediate;
    private ID2D1Factory1? _factory;
    private ID2D1DeviceContext? _context;
    private ID2D1Effect? _effect;
    private ID2D1Image? _output;
    private ID2D1Bitmap1? _target;
    private ID3D11Texture2D? _texture;
    private IsolationShaderEffect? _parameters;
    private readonly bool _shared;
    private bool _disposed;
    public ID3D11Texture2D Texture => _texture ?? throw new InvalidOperationException("Isolation target is not initialized.");
    public IsolationShaderEffect Parameters => _parameters ?? throw new InvalidOperationException("Isolation effect is not initialized.");
    public IsolationGraphics(bool shared = true, bool software = false)
    {
        _shared = shared;
        try
        {
            D3D11.D3D11CreateDevice(IntPtr.Zero, software ? DriverType.Warp : DriverType.Hardware, DeviceCreationFlags.BgraSupport, [Vortice.Direct3D.FeatureLevel.Level_11_0], out _device).CheckError();
            _immediate = _device!.ImmediateContext;
            using var dxgi = _device.QueryInterface<IDXGIDevice>();
            _factory = D2D1.D2D1CreateFactory<ID2D1Factory1>();
            using var device = _factory.CreateDevice(dxgi);
            _context = device.CreateDeviceContext();
            _factory.RegisterEffect<IsolationShaderEffect>(() => _parameters = new IsolationShaderEffect());
            _effect = new ID2D1Effect(_context.CreateEffect(typeof(IsolationShaderEffect).GUID));
            _output = _effect.Output;
            _context.UnitMode = UnitMode.Pixels;
            _context.PrimitiveBlend = PrimitiveBlend.Copy;
            if (_parameters is null)
                throw new InvalidOperationException("Isolation callback was not created.");
        }
        catch { Dispose(); throw; }
    }
    public void Resize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (width < 1 || height < 1 || width > 16384 || height > 16384)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (_texture is not null && _texture.Description.Width == width && _texture.Description.Height == height)
            return;
        var description = new Texture2DDescription { Width = width, Height = height, MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm, SampleDescription = new(1, 0), Usage = ResourceUsage.Default, BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource, MiscFlags = _shared ? ResourceOptionFlags.Shared : ResourceOptionFlags.None };
        ID3D11Texture2D? texture = null;
        ID2D1Bitmap1? target = null;
        try
        {
            texture = _device!.CreateTexture2D(in description);
            using var surface = texture.QueryInterface<IDXGISurface>();
            target = _context!.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(new D2DPixelFormat(Format.B8G8R8A8_UNorm, D2DAlphaMode.Ignore), 96, 96, BitmapOptions.Target | BitmapOptions.CannotDraw));
            _context.Target = target;
            _target?.Dispose();
            _texture?.Dispose();
            _target = target;
            _texture = texture;
            target = null;
            texture = null;
            Parameters.Resolution = new Vector2(width, height);
        }
        finally { target?.Dispose(); texture?.Dispose(); }
    }
    public void Draw(float time, Vector3[] colors, float dimming)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (colors.Length != 4)
            throw new ArgumentException("Isolation requires four colors.", nameof(colors));
        Parameters.Color1 = colors[0];
        Parameters.Color2 = colors[1];
        Parameters.Color3 = colors[2];
        Parameters.Color4 = colors[3];
        Parameters.Dimming = dimming;
        Parameters.iTime = time;
        _context!.BeginDraw();
        try
        {
            _context.DrawImage(_output!, null, null, Vortice.Direct2D1.InterpolationMode.Linear, CompositeMode.SourceCopy);
        }
        finally { _context.EndDraw().CheckError(); }
        _immediate!.Flush();
        _device!.DeviceRemovedReason.CheckError();
    }
    public byte[] ReadPixels()
    {
        var description = Texture.Description;
        description.Usage = ResourceUsage.Staging;
        description.BindFlags = BindFlags.None;
        description.MiscFlags = ResourceOptionFlags.None;
        description.CPUAccessFlags = CpuAccessFlags.Read;
        using var staging = _device!.CreateTexture2D(in description);
        _immediate!.CopyResource(staging, Texture);
        var mapped = _immediate.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        var pixels = new byte[description.Width * description.Height * 4];
        try
        {
            for (int y = 0; y < description.Height; y++)
                Marshal.Copy(mapped.DataPointer + y * mapped.RowPitch, pixels, y * description.Width * 4, description.Width * 4);
        }
        finally { _immediate.Unmap(staging, 0); }
        return pixels;
    }
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_context is not null)
            _context.Target = null;
        _target?.Dispose();
        _texture?.Dispose();
        _output?.Dispose();
        _effect?.Dispose();
        _parameters?.ReleaseDrawInfo();
        _parameters?.Dispose();
        _context?.Dispose();
        _factory?.Dispose();
        _immediate?.Dispose();
        _device?.Dispose();
        _target = null;
        _texture = null;
        _output = null;
        _effect = null;
        _parameters = null;
        _context = null;
        _factory = null;
        _immediate = null;
        _device = null;
    }
}
