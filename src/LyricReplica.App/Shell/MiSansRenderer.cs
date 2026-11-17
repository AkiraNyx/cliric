using System.Globalization;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using Vortice.WIC;
using Color = System.Windows.Media.Color;
using DWriteWeight = Vortice.DirectWrite.FontWeight;
using DWriteStyle = Vortice.DirectWrite.FontStyle;
using DWriteStretch = Vortice.DirectWrite.FontStretch;

namespace LyricReplica.App;

internal sealed class MiSansRenderer
{
    private static readonly Lazy<MiSansRenderer> Instance = new(() => new MiSansRenderer());
    private readonly IDWriteFactory6 _write = DWrite.DWriteCreateFactory<IDWriteFactory6>();
    private readonly ID2D1Factory _draw = D2D1.D2D1CreateFactory<ID2D1Factory>();
    private readonly IWICImagingFactory _wic = new();
    private readonly IDWriteInMemoryFontFileLoader _loader;
    private readonly IDWriteFontCollection2 _fonts;

    private MiSansRenderer()
    {
        using var resource = Application.GetResourceStream(MiSansTypography.FontResource)!.Stream;
        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        _loader = _write.CreateInMemoryFontFileLoader();
        _write.RegisterFontFileLoader(_loader);
        var pin = GCHandle.Alloc(buffer.GetBuffer(), GCHandleType.Pinned);
        try
        {
            using var face = _loader.CreateInMemoryFontFileReference(_write, pin.AddrOfPinnedObject(), checked((int)buffer.Length), null);
            using var builder = _write.CreateFontSetBuilder();
            builder.AddFontFile(face);
            using var set = builder.CreateFontSet();
            _fonts = _write.CreateFontCollectionFromFontSet(set, FontFamilyModel.Typographic);
        }
        finally { pin.Free(); }
    }

    internal static Size Measure(string text, double fontSize, int weight, int features, Size limit, TextWrapping wrapping, TextTrimming trimming)
    {
        using var layout = Instance.Value.CreateLayout(text, fontSize, weight, features, limit, wrapping, trimming);
        var bounds = layout.Metrics;
        return new Size(Math.Min(limit.Width, bounds.WidthIncludingTrailingWhitespace), Math.Min(limit.Height, bounds.Height));
    }

    internal static BitmapSource Render(string text, double fontSize, int weight, int features, Size size, DpiScale dpi,
        Color color, TextWrapping wrapping, TextTrimming trimming)
    {
        var self = Instance.Value;
        using var layout = self.CreateLayout(text, fontSize, weight, features, size, wrapping, trimming);
        int width = Math.Max(1, (int)Math.Ceiling(size.Width * dpi.DpiScaleX));
        int height = Math.Max(1, (int)Math.Ceiling(size.Height * dpi.DpiScaleY));
        using var bitmap = self._wic.CreateBitmap(width, height, Vortice.WIC.PixelFormat.Format32bppPBGRA, BitmapCreateCacheOption.CacheOnLoad);
        var properties = new RenderTargetProperties(RenderTargetType.Default,
            new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            (float)dpi.PixelsPerInchX, (float)dpi.PixelsPerInchY, RenderTargetUsage.None, FeatureLevel.Default);
        using var target = self._draw.CreateWicBitmapRenderTarget(bitmap, properties);
        using var brush = target.CreateSolidColorBrush(new Color4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f));

        target.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Grayscale;
        target.BeginDraw();
        target.Clear(new Color4(0, 0, 0, 0));
        target.DrawTextLayout(Vector2.Zero, layout, brush, DrawTextOptions.Clip);
        target.EndDraw().CheckError();

        int stride = width * 4;
        var pixels = new byte[checked(stride * height)];
        bitmap.CopyPixels(stride, pixels);
        var image = BitmapSource.Create(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32, null, pixels, stride);
        image.Freeze();
        return image;
    }

    private IDWriteTextLayout CreateLayout(string text, double size, int weight, int features, Size limit, TextWrapping wrapping, TextTrimming trimming)
    {
        FontAxisValue[] axes = [new FontAxisValue { AxisTag = FontAxisTag.Weight, Value = weight }];
        using var format = _write.CreateTextFormat(MiSansTypography.FamilyName, _fonts, axes, axes.Length,
            (float)size, CultureInfo.CurrentUICulture.Name);
        format.WordWrapping = wrapping == TextWrapping.NoWrap ? WordWrapping.NoWrap : WordWrapping.Wrap;
        var layout = _write.CreateTextLayout(text, format, Dimension(limit.Width), Dimension(limit.Height));
        try
        {
            if (features != 0 && text.Length > 0)
            {
                using var typography = _write.CreateTypography();
                for (int i = 0; i < MiSansTypography.Features.Length; i++)
                {
                    if ((features & (1 << i)) == 0)
                        continue;
                    string tag = MiSansTypography.Features[i].Tag;
                    var nativeTag = (FontFeatureTag)(tag[0] | tag[1] << 8 | tag[2] << 16 | tag[3] << 24);
                    typography.AddFontFeature(new FontFeature { NameTag = nativeTag, Parameter = 1 });
                }
                layout.SetTypography(typography, new TextRange(0, text.Length));
            }
            if (trimming != TextTrimming.None)
            {
                using var ellipsis = _write.CreateEllipsisTrimmingSign(format);
                layout.SetTrimming(new Trimming
                {
                    Granularity = trimming == TextTrimming.WordEllipsis ? TrimmingGranularity.Word : TrimmingGranularity.Character
                }, ellipsis);
            }
            return layout;
        }
        catch
        {
            layout.Dispose();
            throw;
        }

        static float Dimension(double value) => double.IsFinite(value) ? (float)Math.Max(1, value) : float.MaxValue;
    }
}
