using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cliric.Core;

static class SettingsChecks
{
    private static string LocalEnumName(string referenceName) => referenceName.Replace("Lyricify", "Cliric", StringComparison.Ordinal);

    public static void Run(Action<bool, string> check)
    {
        var renamed = SettingsFile.Parse("""{"UseLyricifyParser":true}""");
        check(renamed.UseCliricExtendedParser &&
            !SettingsFile.Parse("""{"UseCliricExtendedParser":false,"UseLyricifyParser":true}""").UseCliricExtendedParser &&
            JsonSerializer.Serialize(renamed).Contains("\"UseCliricExtendedParser\":true", StringComparison.Ordinal),
            "legacy parser preferences load and serialize under the Cliric setting");
        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/settings-original.json")));
        var original = contract.RootElement;
        var settings = new AppleMusicSettings();
        foreach (var expected in original.GetProperty("defaults").EnumerateArray())
        {
            string propertyPath = expected.GetProperty("property").GetString()!;
            object? value = settings;
            foreach (string segment in propertyPath.Split('.'))
                value = value!.GetType().GetProperty(segment)!.GetValue(value);
            var target = expected.GetProperty("value");
            bool equal = target.ValueKind switch
            {
                JsonValueKind.Null => value is null,
                JsonValueKind.True or JsonValueKind.False => Equals(value, target.GetBoolean()),
                JsonValueKind.Number => value is not null && Math.Abs(Convert.ToDouble(value) - target.GetDouble()) < 1e-9,
                _ => value is double d ? double.IsNaN(d) && target.GetString() == "NaN" : value is Enum e ? Equals(e, Enum.Parse(e.GetType(), LocalEnumName(target.GetString()!))) : Equals(value, target.GetString())
            };
            check(equal, "reference setting default " + propertyPath);
        }
        object[] instances = [settings, settings.BackgroundAppleMusicOriginal, settings.BuggyAppleOptions, settings.ZoomingLevels];
        int typeIndex = 0;
        foreach (var schema in original.GetProperty("types").EnumerateArray())
        {
            var instance = instances[typeIndex++];
            var properties = instance.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            check(properties.Length == schema.GetProperty("properties").GetArrayLength(), "reference setting property count");
            using var saved = JsonDocument.Parse(JsonSerializer.Serialize(instance, instance.GetType()));
            foreach (var property in schema.GetProperty("properties").EnumerateArray())
            {
                string name = property.GetProperty("name").GetString()!;
                var actual = instance.GetType().GetProperty(name)!;
                bool ignored = property.GetProperty("jsonIgnored").GetBoolean();
                check((actual.GetCustomAttribute<JsonIgnoreAttribute>() is not null) == ignored, "setting persistence role " + name);
                if (ignored)
                    continue;
                string key = property.GetProperty("jsonPropertyName").GetString()!;
                check(actual.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name == key && saved.RootElement.TryGetProperty(key, out _), "reference setting JSON key " + name);
                if (property.GetProperty("enumMembers").ValueKind == JsonValueKind.Array)
                    foreach (var member in property.GetProperty("enumMembers").EnumerateArray())
                        check(Convert.ToInt64(Enum.Parse(actual.PropertyType, LocalEnumName(member.GetProperty("name").GetString()!))) == member.GetProperty("value").GetInt64(), "reference setting enum " + name);
            }
        }
        foreach (var preset in original.GetProperty("stylePresets").EnumerateArray())
        {
            int index = preset.GetProperty("index").GetInt32();
            settings.ApplyStylePreset(index);
            int[] expected = preset.GetProperty("indices").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            check(settings.GetStyleSelections().SequenceEqual(expected) && settings.GetStylePreset() == index, "original 12-option style preset " + index);
            check(settings.LineSwitchingAppleIosStyle164 == (expected[1] == 0) && settings.LineSwitchingOpacitySpeedAppleIosStyle164 == (expected[2] == 0), "original style boolean direction " + index);
            check(settings.BackgroundVocalsEnterDirectionLeft == (expected[5] == 2) && settings.BackgroundVocalsEnterAnimation == (expected[5] switch
            {
                0 => 0,
                3 => 2,
                _ => 1
            }), "original background flyout mapping " + index);
            string json = JsonSerializer.Serialize(settings);
            var restored = JsonSerializer.Deserialize<AppleMusicSettings>(json)!;
            check(restored.GetStyleSelections().SequenceEqual(expected) && double.IsNaN(restored.KaraokeStyleProgressWidth), "preset and sentinel persistence " + index);
        }
        settings = new();
        settings.MagicStripTutorial = false;
        check(!settings.MagicStripStatus, "nullable tutorial fallback");
        settings.MagicStripStatus = true;
        var roundtrip = JsonSerializer.Deserialize<AppleMusicSettings>(JsonSerializer.Serialize(settings))!;
        check(roundtrip.magicStripStatus == true && roundtrip.MagicStripStatus, "effective notice writes backing setting");
        settings.WordShineEffectPreset = (WordShineEffectPresetTypes)99;
        settings.InterLineAnimationPreset = (InterLineAnimationPresetTypes)(-1);
        settings.TranslationLinePreset = (TranslationLinePresetTypes)99;
        check(settings.WordShineEffectPreset == WordShineEffectPresetTypes.iOS162 && settings.InterLineAnimationPreset == InterLineAnimationPresetTypes.iOS && settings.TranslationLinePreset == TranslationLinePresetTypes.Cliric, "invalid enum setters match reference fallback");
        for (int i = 0; i < 6; i++)
        {
            settings.SetPullingPower(i);
            check(settings.GetPullingPower() == i, "pulling selection " + i);
        }
        settings.PullingDelay = 1.7;
        check(settings.GetPullingPower() == -1, "custom pulling delay preserved");
        settings.SetFloatSelection(4);
        check(settings.GetFloatHeight() == 4, "float UI level converted to half-height");
        settings.SetFloatSelection(3);
        check(settings.GetFloatHeight() == 2.75, "float default sentinel");
        settings.KaraokeStyleProgressWidthPreset = 2;
        check(settings.GetProgressWidth(false) == 64 && settings.GetProgressWidth(true) == 72, "original wide sweep preset");
        settings.KaraokeStyleProgressWidth = 12;
        check(Math.Abs(settings.GetProgressWidth(true) - 9.6) < 1e-9, "explicit sweep width overrides preset");
        const string legacy = """{"blur":true,"Bold":true,"Float":false,"Preset":2,"Background":2,"FontFamily":"Test Font","Volume":0.4}""";
        var migrated = SettingsFile.Parse(legacy);
        check(migrated.AppleMusicLyrics.BlurLyrics && migrated.AppleMusicLyrics.BoldText && !migrated.AppleMusicLyrics.KaraokeStyleFloat && migrated.AppleMusicLyrics.WordShineEffectPreset == WordShineEffectPresetTypes.Android, "legacy flat settings migrate into reference settings");
        check(migrated.AppleMusicLyrics.Background == BackgroundTypes.Dynamic && migrated.AppleMusicLyrics.FontFamily == "Test Font" && migrated.Volume == .4, "legacy background font and host settings survive migration");
        var authoritative = SettingsFile.Parse("""{"Blur":true,"AppleMusicLyrics":{"blur_lyrics":false,"magic_strip_status":false}}""");
        check(!authoritative.Blur && !authoritative.AppleMusicLyrics.MagicStripStatus, "nested contract is authoritative over obsolete flat keys");
        var repaired = SettingsFile.Parse("""{"AppleMusicLyrics":{"buggy_apple_options":null,"zooming_levels":null,"background_apple_music_original":null}}""");
        check(repaired.AppleMusicLyrics.BuggyAppleOptions is not null && repaired.AppleMusicLyrics.ZoomingLevels is not null && repaired.AppleMusicLyrics.BackgroundAppleMusicOriginal is not null, "null sub-settings are repaired before consumers");
        bool invalidRoot = false;
        try
        {
            SettingsFile.Parse("[]");
        }
        catch (JsonException) { invalidRoot = true; }
        check(invalidRoot, "invalid settings root reports JSON error");
        string directory = Path.Combine(AppContext.BaseDirectory, "settings-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        try
        {
            File.WriteAllText(path, legacy);
            SettingsFile.Save(path, migrated);
            check(File.ReadAllText(path + ".bak") == legacy, "atomic settings save preserves the previous profile");
            var persisted = SettingsFile.Parse(File.ReadAllText(path));
            check(persisted.Blur && !persisted.Float && persisted.AppleMusicLyrics.BuggyAppleOptions.BackgroundVocalsOverlap, "reference settings survive file roundtrip");
            using var saved = JsonDocument.Parse(File.ReadAllText(path));
            check(saved.RootElement.TryGetProperty("AppleMusicLyrics", out _) && !saved.RootElement.TryGetProperty("Blur", out _), "save has one authoritative configuration path");
            File.WriteAllText(path, "{");
            int errors = 0;
            var recovered = SettingsFile.Load(path, _ => errors++);
            check(errors == 1 && recovered.Blur && !recovered.Float, "malformed current profile recovers from the valid backup and reports the failure");
            SettingsFile.Save(path, migrated);
            check(File.ReadAllText(path + ".bak") == legacy, "saving after a corrupt profile preserves the last valid backup");
            check(File.ReadAllText(path + ".invalid.bak") == "{", "corrupt profile is retained separately");
        }
        finally { foreach (string file in Directory.EnumerateFiles(directory)) File.Delete(file); Directory.Delete(directory); }
    }
}
