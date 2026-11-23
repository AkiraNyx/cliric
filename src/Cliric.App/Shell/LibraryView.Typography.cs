using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Cliric.App;

public sealed partial class LibraryView
{
    private sealed record PlayerTypography(int Weight, int Features);

    private readonly CheckBox[] _fontFeatures = new CheckBox[MiSansTypography.Features.Length];
    private readonly DispatcherTimer _typographyCommit = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(350) };
    private PlayerTypography? _pendingTypography;
    private static string TypographySettingsPath => Path.Combine(App.DataFolder, "player-typography.json");

    private void InitializeTypography()
    {
        var saved = ReadTypography();
        ApplyPlayerTypography(saved);

        for (int i = 0; i < _fontFeatures.Length; i++)
        {
            var feature = MiSansTypography.Features[i];
            int flag = 1 << i;
            var check = new CheckBox
            {
                Content = feature.Label + " (" + feature.Tag + ")",
                IsChecked = (saved.Features & flag) != 0,
                MinWidth = 186,
                Margin = new Thickness(0, 2, 16, 2),
                Tag = flag
            };
            check.Click += OnFontFeatureClicked;
            _fontFeatures[i] = check;
            FontFeatureList.Children.Add(check);
        }
        FontWeightSlider.Value = saved.Weight;
        FontWeightSlider.AttachValueEditor(FontWeightValue, 0);
        FontWeightSlider.ValueChanged += (_, _) => UpdateTypography();
        FontWeightSlider.IsMouseCaptureWithinChanged += (_, e) =>
        {
            if (e.NewValue is false)
                UpdateTypography();
        };
        _typographyCommit.Tick += (_, _) => SavePendingTypography();
        _lyrics.Closed += (_, _) => SavePendingTypography();
        FontPreviewInput.Text = "MiSans 界面字体 · 12:34 64GB 23℃ “音乐” *# 1/2 H2O (ABC) 1111 8888 「播放」。";
        UpdateTypography();
    }

    private void OnFontFeatureClicked(object sender, RoutedEventArgs e)
    {
        var selected = (CheckBox)sender;
        if (selected.IsChecked == true && selected.Tag is int flag)
        {
            int other = flag == MiSansTypography.Superscript ? MiSansTypography.Subscript :
                flag == MiSansTypography.Subscript ? MiSansTypography.Superscript : 0;
            if (other != 0)
                _fontFeatures.First(check => (int)check.Tag == other).IsChecked = false;
        }
        UpdateTypography();
    }

    private void UpdateTypography()
    {
        var typography = ReadTypographyControls();
        FontPreview.FontWeight = FontWeight.FromOpenTypeWeight(typography.Weight);
        MiSansTypography.Apply(FontPreview, typography.Features);
        if (FontWeightSlider.IsMouseCaptureWithin ||
            (typography.Weight == FontWeight.ToOpenTypeWeight() && typography.Features == MiSansTypography.GetFeatures(this)))
            return;

        ApplyPlayerTypography(typography);
        _pendingTypography = typography;
        _typographyCommit.Stop();
        _typographyCommit.Start();
    }

    private PlayerTypography ReadTypographyControls()
    {
        int mask = 0;
        foreach (var check in _fontFeatures)
            if (check.IsChecked == true)
                mask |= (int)check.Tag;
        return new PlayerTypography((int)FontWeightSlider.Value, mask);
    }

    private void ApplyPlayerTypography(PlayerTypography typography)
    {
        FontWeight = FontWeight.FromOpenTypeWeight(typography.Weight);
        Resources["Shell.SemiboldWeight"] = FontWeight.FromOpenTypeWeight(Math.Min(typography.Weight + 190, MiSansTypography.MaximumWeight));
        Resources["Shell.BoldWeight"] = FontWeight.FromOpenTypeWeight(Math.Min(typography.Weight + 300, MiSansTypography.MaximumWeight));
        MiSansTypography.Apply(this, typography.Features);
    }

    private void SavePendingTypography()
    {
        _typographyCommit.Stop();
        if (_pendingTypography is not { } typography)
            return;
        try
        {
            string pending = TypographySettingsPath + "." + Environment.ProcessId + ".tmp";
            File.WriteAllText(pending, JsonSerializer.Serialize(typography));
            File.Move(pending, TypographySettingsPath, overwrite: true);
            _pendingTypography = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ShowNotice("无法保存界面字体设置");
            App.Log(error.ToString());
        }
    }

    private static PlayerTypography ReadTypography()
    {
        try
        {
            if (File.Exists(TypographySettingsPath) &&
                JsonSerializer.Deserialize<PlayerTypography>(File.ReadAllText(TypographySettingsPath)) is { } saved)
            {
                int flags = saved.Features & MiSansTypography.AllFeatures;
                if ((flags & MiSansTypography.Superscript) != 0)
                    flags &= ~MiSansTypography.Subscript;
                return new PlayerTypography(Math.Clamp(saved.Weight, MiSansTypography.MinimumWeight, MiSansTypography.MaximumWeight), flags);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            App.Log(error.ToString());
        }
        return new PlayerTypography(MiSansTypography.DefaultWeight, 0);
    }
}
