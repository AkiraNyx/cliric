using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Vortice.Direct3D11;
using Vortice.Direct3D9;
using IDXGIResource = Vortice.DXGI.IDXGIResource;

namespace LyricReplica.App.Reference;

internal sealed class IsolationSurfaceBridge : IDisposable
{
    private IDirect3D9Ex? _d3d;
    private IDirect3DDevice9Ex? _device;
    private IDirect3DTexture9? _texture;
    private IntPtr _attached;
    private int _width, _height;
    private double _dpiX, _dpiY;
    public D3DImage Source { get; private set; } = new();
    public IsolationSurfaceBridge()
    {
        try
        {
            _d3d = D3D9.Direct3DCreate9Ex();
            var parameters = new PresentParameters { Windowed = true, SwapEffect = SwapEffect.Flip, DeviceWindowHandle = GetDesktopWindow(), PresentationInterval = PresentInterval.Default };
            _device = _d3d.CreateDeviceEx(0, DeviceType.Hardware, IntPtr.Zero, CreateFlags.FpuPreserve | CreateFlags.Multithreaded | CreateFlags.HardwareVertexProcessing, parameters);
        }
        catch { Dispose(); throw; }
    }
    public void Attach(ID3D11Texture2D texture, double dpiX, double dpiY)
    {
        if (texture.NativePointer == _attached && Math.Abs(dpiX - _dpiX) < .01 && Math.Abs(dpiY - _dpiY) < .01)
            return;
        using var resource = texture.QueryInterface<IDXGIResource>();
        IntPtr shared = resource.SharedHandle;
        var description = texture.Description;
        var next = _device!.CreateTexture(description.Width, description.Height, 1, Vortice.Direct3D9.Usage.RenderTarget, Vortice.Direct3D9.Format.A8R8G8B8, Pool.Default, ref shared);
        try
        {
            var image = new D3DImage(dpiX, dpiY);
            using var surface = next.GetSurfaceLevel(0);
            image.Lock();
            try
            {
                image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, surface.NativePointer, true);
            }
            finally { image.Unlock(); }
            Detach();
            _texture?.Dispose();
            _texture = next;
            Source = image;
            _attached = texture.NativePointer;
            _width = description.Width;
            _height = description.Height;
            _dpiX = dpiX;
            _dpiY = dpiY;
        }
        catch { next.Dispose(); throw; }
    }
    public void Draw(Action draw)
    {
        if (_texture is null)
            throw new InvalidOperationException("Isolation shared surface is not bound.");
        Source.Lock();
        try
        {
            draw();
            Source.AddDirtyRect(new Int32Rect(0, 0, _width, _height));
        }
        finally { Source.Unlock(); }
    }
    private void Detach()
    {
        if (Source.PixelWidth == 0)
            return;
        Source.Lock();
        try
        {
            Source.SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero);
        }
        finally { Source.Unlock(); }
    }
    public void Dispose()
    {
        try
        {
            Detach();
        }
        finally { _texture?.Dispose(); _device?.Dispose(); _d3d?.Dispose(); _texture = null; _device = null; _d3d = null; _attached = IntPtr.Zero; }
    }
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
}
