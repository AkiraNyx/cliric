using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using LyricReplica.Core;

namespace LyricReplica.App;

internal static class SettingsPanelChecks
{
    public static void Run(Action<bool, string> check)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("settings-controls-original.json")!;
        using var fixture = JsonDocument.Parse(stream);
        var options = new ViewOptions();
        int changes = 0;
        var panel = new SettingsPanel(options, _ => changes++, () => { });
        var controls = new Dictionary<string, FrameworkElement>();
        Walk(panel);
        void Walk(DependencyObject node)
        {
            if (node is FrameworkElement element && !string.IsNullOrEmpty(element.Name))
                controls.Add(element.Name, element);
            foreach (object child in LogicalTreeHelper.GetChildren(node))
            if (child is DependencyObject dependency)
                Walk(dependency);
        }
        check(fixture.RootElement.GetArrayLength() == 50, "original main settings control baseline");
        check(!controls.ContainsKey("HostRealtimeSeek"), "player seek preference is separate from lyric settings");
        var track = new NetEaseTrack("", 1, "Sample", "Artist", "Album", 30000, true, ClientSession: true);
        var payload = JsonSerializer.SerializeToElement(new
        {
            lrc = new { lyric = "[00:01.00]line fallback" },
            yrc = new { lyric = "[1000,900](1000,400,0)one (1400,500,0)two" },
            tlyric = new { lyric = "[00:01.00]translation" }
        });
        var parsed = NetEaseMusic.ParseClientLyrics(track, payload).Lines.Single();
        check(parsed.Text == "one two" && parsed.Words.Count == 2 && parsed.Words[1].StartMs == 1400 &&
            parsed.Words[1].DurationMs == 500 && parsed.Translation == "translation", "NetEase YRC retains absolute word timings and aligned translation");
        var jsonOptions = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        foreach (var reference in fixture.RootElement.EnumerateArray())
        {
            string name = reference.GetProperty("name").GetString()!;
            check(controls.TryGetValue(name, out var control), "original settings control exists " + name);
            if (name == "ToggleSwitchEnableAppleMusicLyricsTraditional")
            {
                check(!control!.IsEnabled, "traditional-client entry is explicitly unavailable in the independent page");
                continue;
            }
            if (name == "ButtonResetFont_AppleMusicLyrics")
                options.AppleMusicLyrics.FontFamily = "Test Font";
            if (name == "ButtonCustomFont_AppleMusicLyrics")
                ((ComboBox)controls["HostCustomFontFamily"]).Text = "Segoe UI";
            var references = reference.GetProperty("properties").EnumerateArray().Select(x => x.GetString()!).ToArray();
            var before = references.ToDictionary(x => x, Read);
            int previousChanges = changes;
            switch (control)
            {
                case CheckBox toggle:
                    toggle.IsChecked = toggle.IsChecked != true;
                    toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    break;
                case ComboBox combo:
                    combo.SelectedIndex = combo.SelectedIndex == 0 ? 1 : 0;
                    break;
                case Slider slider:
                    slider.Value = slider.Value == slider.Maximum ? slider.Minimum : slider.Maximum;
                    break;
                case Button button:
                    button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    break;
                default:
                    throw new InvalidOperationException("Unexpected settings control " + name);
            }
            check(changes == previousChanges + 1, "settings event applies once " + name);
            check(references.Any(property => before[property] != Read(property)), "settings event reaches original property " + name);
        }
        string Read(string path)
        {
            object? value = options.AppleMusicLyrics;
            foreach (string member in path.Split('.'))
                value = value!.GetType().GetProperty(member)!.GetValue(value);
            return JsonSerializer.Serialize(value, value?.GetType() ?? typeof(object), jsonOptions);
        }
    }
}
