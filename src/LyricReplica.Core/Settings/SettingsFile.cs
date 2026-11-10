using System.Text.Json;

namespace LyricReplica.Core;

public static class SettingsFile
{
    public static ViewOptions Load(string path, Action<Exception> report)
    {
        if (File.Exists(path))
        {
            try
            {
                return Parse(File.ReadAllText(path));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { report(exception); }
        }
        return File.Exists(path + ".bak") ? Parse(File.ReadAllText(path + ".bak")) : new();
    }
    public static ViewOptions Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("设置必须是 JSON 对象。");
        var options = JsonSerializer.Deserialize<ViewOptions>(json, LyricsFile.JsonOptions) ?? new();
        if (!document.RootElement.EnumerateObject().Any(p => p.Name.Equals(nameof(ViewOptions.RomajiOption), StringComparison.OrdinalIgnoreCase)))
            Copy<bool>("Romanization", v => options.Romanization = v);
        if (!document.RootElement.EnumerateObject().Any(p => p.Name.Equals(nameof(ViewOptions.AppleMusicLyrics), StringComparison.OrdinalIgnoreCase)))
        {
            // Older replica profiles stored these values flat; migrate them before writing the nested contract.
            Copy<bool>("Blur", v => options.Blur = v);
            Copy<bool>("Bold", v => options.Bold = v);
            Copy<double>("BlurLevel", v => options.BlurLevel = v);
            Copy<bool>("Duet", v => options.Duet = v);
            Copy<bool>("RightAlignFirst", v => options.RightAlignFirst = v);
            Copy<bool>("WordShine", v => options.WordShine = v);
            Copy<bool>("Float", v => options.Float = v);
            Copy<bool>("ShowFps", v => options.ShowFps = v);
            Copy<bool>("AnimatedTime", v => options.AnimatedTime = v);
            Copy<double>("AnimationSpeed", v => options.AnimationSpeed = v);
            Copy<MotionPreset>("Preset", v => options.Preset = v);
            Copy<BackgroundMode>("Background", v => options.Background = v);
            Copy<string>("FontFamily", v => options.FontFamily = v);
        }
        options.Normalize();
        return options;
        void Copy<T>(string name, Action<T> set)
        {
            foreach (var property in document.RootElement.EnumerateObject())
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    set(property.Value.Deserialize<T>(LyricsFile.JsonOptions)!);
                    return;
                }
        }
    }
    public static void Save(string path, ViewOptions options)
    {
        options.Normalize();
        string temporary = path + ".tmp", backup = path + ".bak";
        if (File.Exists(path))
        {
            try
            {
                Parse(File.ReadAllText(path));
            }
            catch (JsonException) { backup = path + ".invalid.bak"; }
        }
        File.WriteAllText(temporary, JsonSerializer.Serialize(options, LyricsFile.JsonOptions));
        if (File.Exists(path))
            File.Replace(temporary, path, backup);
        else
            File.Move(temporary, path);
    }
}
