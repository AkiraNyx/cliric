using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LyricReplica.Core;

namespace LyricReplica.App;

public static class Artwork
{
    internal const int MaximumMediaBytes = 16 * 1024 * 1024;
    public static BitmapSource Create(int index)
    {
        Color[][] colors = [[Color.FromRgb(28,74,164),Color.FromRgb(106,47,139),Color.FromRgb(213,107,153)],
            [Color.FromRgb(21,92,87),Color.FromRgb(35,47,89),Color.FromRgb(190,160,113)],
            [Color.FromRgb(129,47,36),Color.FromRgb(47,33,77),Color.FromRgb(233,152,73)]];
        var palette = colors[index % colors.Length];
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(palette[0], palette[1], 35), null, new Rect(0, 0, 640, 640));
            for (int i = 0; i < 7; i++)
            {
                var gradient = new RadialGradientBrush(Color.FromArgb((byte)(170 - i * 10), palette[2].R, palette[2].G, palette[2].B), Colors.Transparent);
                dc.DrawEllipse(gradient, null, new Point(80 + i * 94, 140 + Math.Sin(i * 1.7) * 170 + index * 35), 150 + i * 15, 230);
            }
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(110, 255, 245, 235)), 2);
            for (int i = 0; i < 6; i++)
            {
                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    ctx.BeginFigure(new Point(-100, 440 + i * 25), false, false);
                    ctx.BezierTo(new Point(190, 200 + i * 20), new Point(360, 620 - i * 40), new Point(760, 190 + i * 35), true, false);
                }
                dc.DrawGeometry(null, pen, geometry);
            }
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(220, 239, 224, 211)), null, new Point(450, 175), 42, 42);
        }
        var bitmap = new RenderTargetBitmap(640, 640, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
    public static byte[] Encode(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
    public static BitmapSource Load(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(Path.GetFullPath(path));
        image.DecodePixelWidth = 1200;
        image.EndInit();
        image.Freeze();
        return image;
    }
    internal static BitmapSource Decode(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaximumMediaBytes)
            throw new InvalidDataException("媒体封面长度无效。");
        using var stream = new MemoryStream(bytes, false);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.DecodePixelWidth = 1200;
        image.EndInit();
        image.Freeze();
        return image;
    }
}

public static class DemoSongs
{
    public static List<Song> Create()
    {
        var first = new Song { Id = "demo-light", Title = "沿光而行", Artist = "Lyric Lab", Album = "夜航 · 原创测试歌词", DurationMs = 93000, Credit = "Written by Lyric Lab" };
        Add(first, "让每一个字都慢慢亮起", 7000, 6000, "Let every word find its light", "Ràng měi yí gè zì dōu mànmàn liàng qǐ");
        Add(first, "We follow the light", 13500, 6500, "我们循着光前行", "");
        Add(first, "穿过夜色 与你同行", 21000, 6500, "Through the night, we walk together", "");
        Add(first, "把此刻写进旋律里", 28000, 6000, "Write this moment into a melody", "");
        Add(first, "A little brighter, a little closer", 34500, 8500, "更亮一点，更近一点", "");
        Add(first, "直到所有声音 再次相遇", 44000, 7000, "Until all our voices meet again", "");
        Add(first, "停一拍 听风经过", 59000, 6500, "Pause a beat and listen to the wind", "");
        Add(first, "Every syllable has a place", 66500, 7500, "每个音节都有自己的位置", "");
        Add(first, "让每一个字都慢慢亮起", 75000, 7000, "Let every word find its light", "");
        Add(first, "沿着光 继续前行", 83000, 6000, "Keep walking with the light", "");
        var duet = new Song { Id = "demo-duet", Title = "回声之间", Artist = "Voice A / Voice B", Album = "双声部测试", DurationMs = 69000, Credit = "Words & music · Lyric Lab" };
        Add(duet, "你在这一侧轻轻呼唤", 5000, 6500, "A voice from this side", "", true);
        Add(duet, "I hear you from the other side", 9500, 8500, "另一侧的我听见了", "", false);
        Add(duet, "让两道声音交错", 18500, 6500, "Let our voices intertwine", "", true);
        Add(duet, "(在光里相逢)", 20500, 6500, "", "", true, true);
        Add(duet, "(and find our way)", 22000, 5500, "", "", true, true);
        Add(duet, "Closer, closer", 27500, 6500, "再近一点", "", false);
        Add(duet, "同一个节拍 不同的方向", 35000, 7000, "One rhythm, two directions", "", true);
        Add(duet, "We answer in harmony", 43000, 6500, "在和声中回应", "", false);
        Add(duet, "(在回声之间)", 44500, 6000, "", "", false, true);
        Add(duet, "这一刻 我们都在", 58000, 6500, "We are both here", "", true);
        var syllables = new Song { Id = "demo-syllable", Title = "Slow Motion", Artist = "Lyric Lab", Album = "逐字与间隔测试", DurationMs = 52000, Credit = "Built for lyric motion" };
        Add(syllables, "Hello, beautiful world", 4000, 7000, "你好，美好的世界", "");
        syllables.Lines[0].Words[0].Syllables = [new() { Text = "Hel", StartMs = 4000, DurationMs = 800 }, new() { Text = "lo, ", StartMs = 5400, DurationMs = 600 }];
        Add(syllables, "把时间放慢一点", 12000, 6500, "Let the moment linger", "");
        Add(syllables, "Let the colors flow", 19500, 7000, "让颜色缓缓流动", "");
        Add(syllables, "暂停以后 仍从这里开始", 28000, 7000, "Pause, then begin right here", "");
        Add(syllables, "We move in slow motion", 36000, 9000, "我们在慢镜头中前行", "");
        foreach (var song in new[] { first, duet, syllables })
            song.Validate();
        return [first, duet, syllables];
    }
    private static void Add(Song song, string text, double start, double duration, string translation, string roman, bool? left = null, bool backing = false)
    {
        var parts = System.Text.RegularExpressions.Regex.Matches(text, @"[A-Za-z]+[,!?.]?\s*|[^A-Za-z]").Select(x => x.Value).ToList();
        double weight = parts.Sum(x => Math.Max(1, x.Trim().Length));
        double at = start;
        var line = new LyricLine { Text = text, StartMs = start, EndMs = start + duration, Translation = translation, Romanization = roman, AlignLeft = left, Background = backing };
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part) && line.Words.Count > 0)
            {
                line.Words[^1].Text += part;
                continue;
            }
            double length = duration * Math.Max(1, part.Trim().Length) / weight;
            line.Words.Add(new LyricWord { Text = part, StartMs = at, DurationMs = length });
            at += length;
        }
        song.Lines.Add(line);
    }
}
