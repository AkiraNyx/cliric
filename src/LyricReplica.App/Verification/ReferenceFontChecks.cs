using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using LyricReplica.App.Reference;
using LyricReplica.Core;

namespace LyricReplica.App;

static class ReferenceFontChecks
{
    public static void Run(Action<bool, string> check)
    {
        using var fixture = JsonDocument.Parse(typeof(ReferenceFontChecks).Assembly.GetManifestResourceStream("reference-fonts.json")!);
        foreach (var font in fixture.RootElement.EnumerateArray())
        {
            string file = font.GetProperty("file").GetString()!;
            var uri = new Uri("pack://application:,,,/LyricReplica;component/Assets/Fonts/" + file);
            using var resource = Application.GetResourceStream(uri)!.Stream;
            check(Convert.ToHexString(SHA256.HashData(resource)) == font.GetProperty("sha256").GetString(), "embedded font matches the unchanged reference asset " + file);
            var face = new GlyphTypeface(uri);
            check(face.CharacterToGlyphMap.Count > 0 && face.Weight.ToOpenTypeWeight() == font.GetProperty("weight").GetInt32(), "target runtime decodes the embedded reference face " + file);
        }
        foreach (var item in new[] { (Preset: FontPresets.SpotifyMix, File: "spotifymix"), (Preset: FontPresets.SpotifyCircular, File: "circularspuiv3t"), (Preset: FontPresets.SFPro, File: "sf-pro-display") })
            foreach (var weight in new[] { FontWeights.Normal, FontWeights.Bold })
                CheckDrawing(ReferencePageView.FontFor(item.Preset), weight, item.File);
        CheckDrawing(ReferencePageView.AppleTextFont(), FontWeights.Normal, "sf-pro-text");
        CheckDrawing(ReferencePageView.AppleTextFont(), FontWeights.Bold, "sf-pro-text");
        check(ReferencePageView.FontFor(FontPresets.Custom, "Consolas").Source == "Consolas", "custom font preset keeps the requested family");
        void CheckDrawing(FontFamily family, FontWeight weight, string expected)
        {
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
                drawing.DrawText(new FormattedText("Music 0123", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal), 24, Brushes.White, 1), new Point());
            var faces = GlyphFaces(visual.Drawing).ToArray();
            check(faces.Length > 0 && faces.All(face => face.FontUri.AbsoluteUri.Contains("/assets/fonts/", StringComparison.OrdinalIgnoreCase) && face.FontUri.AbsoluteUri.Contains(expected, StringComparison.OrdinalIgnoreCase)), "drawn text selects embedded " + expected + " " + weight);
        }
    }
    private static IEnumerable<GlyphTypeface> GlyphFaces(Drawing drawing)
    {
        if (drawing is GlyphRunDrawing glyph)
            yield return glyph.GlyphRun.GlyphTypeface;
        else if (drawing is DrawingGroup group)
        foreach (var child in group.Children)
        foreach (var face in GlyphFaces(child))
            yield return face;
    }
}
