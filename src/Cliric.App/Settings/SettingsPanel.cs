using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Text.Json;
using Cliric.Core;

namespace Cliric.App;

public sealed class SettingsPanel : StackPanel
{
    private readonly ViewOptions _options;
    private readonly AppleMusicSettings _settings;
    private readonly Action<bool> _changed;
    private readonly List<ComboBox> _styleOptions = [];
    private ComboBox _style = null!;
    private readonly CheckBox _audioReactive;
    private readonly TextBlock _backgroundAudioNote = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 2, 8), Opacity = .7 };
    private bool _updating;
    private readonly Dictionary<string, FrameworkElement> _conditional = [];
    public SettingsPanel(ViewOptions options, Action<bool> changed, Action reset, Action? reload = null)
    {
        _options = options;
        _settings = options.AppleMusicLyrics;
        _changed = changed;
        Heading("背景与文字");
        Choice("ComboBoxAppleMusicLyricsBackground", "背景", ["静态", "动态", "动态律动", "动态流动", "Apple Music 原版（4.4.2）", "Cliric 现代（开源）"], (int)_settings.Background, i => _settings.Background = (BackgroundTypes)i, true);
        _conditional["independent"] = Check("ToggleSwitchAppleMusicLyricsIndependentBackgroundRendering", "独立渲染背景", _settings.EnableIndependentBackgroundRenderer, v => _settings.EnableIndependentBackgroundRenderer = v, true);
        _conditional["palette"] = Choice("ComboBoxAppleMusicLyricsBackground_Isolation_Palette", "取色算法", ["自动", "KMeans", "OctTree", "MedianCut 1", "MedianCut 2"], (int)_settings.BackgroundDynamicIsolationPalettes, i => _settings.BackgroundDynamicIsolationPalettes = (IsolationPalettes)i, true);
        Check("ToggleSwitchAppleMusicLyricsBlurLyrics", "歌词模糊", _settings.BlurLyrics, v => _settings.BlurLyrics = v);
        _conditional["blur"] = Slider("SliderAppleMusicLyricsBlurLevel", "模糊程度", 1, 9, _settings.BlurLevel * 5, 1, v => _settings.BlurLevel = v / 5);
        Check("ToggleSwitchAppleMusicLyricsDisableBlurAtLyricsLineMouseOver", "鼠标悬停时取消模糊", _settings.DisableBlurAtLyricsLineMouseOver, v => _settings.DisableBlurAtLyricsLineMouseOver = v);
        Check("ToggleSwitchAppleMusicLyricsBoldText", "粗体文字", _settings.BoldText, v => _settings.BoldText = v);
        Choice("ComboBoxAppleMusicLyricsFontPreset", "字体预设", ["默认", "Spotify Mix", "Spotify Circular", "SF Pro", "自定义"], (int)_settings.FontPreset, i => _settings.FontPreset = (FontPresets)i);
        var font = new ComboBox { Name = "HostCustomFontFamily", IsEditable = true, ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(x => x).ToArray(), Text = _settings.FontFamily, Margin = new Thickness(0, 7, 0, 5) };
        var customFont = new StackPanel();
        customFont.Children.Add(font);
        customFont.Children.Add(Button("ButtonCustomFont_AppleMusicLyrics", "应用自定义字体", () => { _settings.FontFamily = font.Text; Change(); }));
        customFont.Children.Add(Button("ButtonResetFont_AppleMusicLyrics", "重置自定义字体", () => { _settings.FontFamily = ""; font.Text = ""; Change(); }));
        _conditional["font"] = customFont;
        Children.Add(customFont);
        Heading("逐字歌词");
        Check("ToggleSwitchAppleMusicLyricsKaraokeMode", "逐字歌词", _settings.Karaoke, v => _settings.Karaoke = v);
        Check("ToggleSwitchAppleMusicLyricsKaraokeModeDisableCJK", "中日韩歌词不使用逐字模式", _settings.IsCjkKaraokeDisabled, v => _settings.IsCjkKaraokeDisabled = v);
        Check("ToggleSwitchAppleMusicLyricsKaraokeModeEnableAnimation", "逐字上浮与发光", _settings.KaraokeStyleEnableAnimation, v => _settings.KaraokeStyleEnableAnimation = v);
        Choice("ComboBoxAppleMusicLyricsKaraokeModeAnimationFloat", "上浮动画", ["关闭", "低", "中", "默认", "高"], _settings.GetFloatSelection(), _settings.SetFloatSelection);
        Check("ToggleSwitchAppleMusicLyricsKaraokeModeAnimationWordShine", "词语发光", _settings.KaraokeStyleWordShine, v => _settings.KaraokeStyleWordShine = v);
        Check("ToggleSwitchAppleMusicLyricsShinyCjk", "中日韩词语发光", _settings.ShinyCjk, v => _settings.ShinyCjk = v);
        Check("ToggleSwitchAppleMusicLyricsDuetView", "对唱视图", _settings.DuetView, v => _settings.DuetView = v);
        Check("ToggleSwitchAppleMusicLyricsRightAlignFirst", "优先右对齐", _settings.RightAlignFirst, v => _settings.RightAlignFirst = v);
        Choice("ComboBoxAppleMusicLyricsPullingEffect", "拖拽效果", ["关闭", "很弱", "弱", "默认", "强", "很强"], _settings.GetPullingPower(), _settings.SetPullingPower);
        Heading("显示与操作");
        Check("ToggleSwitchAppleMusicLyricsMagicStripFeatureNotice", "Magic Strip 功能提示", _settings.MagicStripStatus, v => _settings.MagicStripStatus = v);
        Check("ToggleSwitchAppleMusicLyricsShowMagicStripTutorial", "显示 Magic Strip 教程", _settings.MagicStripTutorial ?? true, v => { _settings.MagicStripTutorial = v; _settings.MagicStripTutorialCount = 0; });
        Check("ToggleSwitchAppleMusicEnableAnimatedTime", "时间数字滚动", _settings.EnableAnimatedTimeStatus, v => _settings.EnableAnimatedTimeStatus = v);
        Check("ToggleSwitchAppleMusicZoomingRestore", "记忆缩放", _settings.ZoomingRestore, v => { _settings.ZoomingRestore = v; _settings.ZoomingLevels = new(); });
        Heading("风格与行间动效");
        _style = Choice("ComboBoxAppleMusicLyricsStylePreset", "风格预设", AppleMusicSettings.StylePresetNames, _settings.GetStylePreset(), i => { _settings.ApplyStylePreset(i); RefreshStyle(); });
        string[] ids = ["TransitionWidth", "InlineAnimation", "LineSwitchingOpacitySpeed", "SyllablePreplayOpacity", "BackgroundVocalPosition", "BackgroundVocalFlyout", "MobileLayout", "WordShineEffect", "BreathingDotsAlignment", "HighlightLineZooming", "InterLineAnimation", "TranslationLinePreset"];
        string[] labels = ["扫光过渡宽度", "行内动画", "透明度切换", "未播放字透明度", "背景人声位置", "背景人声出入动画", "竖屏布局", "词语发光风格", "呼吸点对齐", "高亮行缩放", "行间动画", "翻译行风格"];
        string[][] choices = [["iOS 16.4 及以后", "iOS 16.2", "Android"], ["iOS 16.4 及以后", "iOS 16.2"], ["iOS 16.4 及以后", "iOS 16.2"], ["Cliric", "iOS", "Android"], ["iOS 26", "iOS 16"], ["iOS 26 及以后", "iOS 16.3", "iOS 16.2", "Cliric（旧版）"], ["Cliric", "iOS 17.1 及以后", "iOS 16"], ["iOS 26.4", "iOS 16.2", "Android", "Cliric"], ["iOS", "Android"], ["Cliric", "iOS 16.2 及以后", "iOS 15", "Android"], ["iOS", "Cliric"], ["Cliric", "iOS"]];
        int[] selections = _settings.GetStyleSelections();
        for (int i = 0; i < 12; i++)
        {
            int option = i;
            _styleOptions.Add(Choice("ComboBoxAppleMusicLyrics" + ids[i], labels[i], choices[i], selections[i], value => { _settings.SetStyleSelection(option, value); RefreshStyle(); }));
        }
        Choice("ComboBoxAppleMusicLyricsPreventSleep", "阻止休眠", ["歌词页打开时", "仅全屏时"], _settings.PreventSleepFullscreenOnly ? 1 : 0, i => _settings.PreventSleepFullscreenOnly = i == 1);
        Check("ToggleSwitchAppleMusicLyricsFpsOverlay", "显示 FPS", _settings.ShowFpsOverlay, v => _settings.ShowFpsOverlay = v);
        _conditional["fps"] = Choice("ComboBoxAppleMusicLyricsFpsOverlayUpdateInterval", "FPS 更新间隔", ["0.25 秒", "0.5 秒", "1 秒"], _settings.FpsOverlayUpdateInterval <= .25 ? 0 : _settings.FpsOverlayUpdateInterval >= 1 ? 2 : 1, i => _settings.FpsOverlayUpdateInterval = new[] { .25, .5, 1 }[i]);
        Heading("旧版兼容与调试");
        Check("ToggleSwitchAppleMusicLyricsBuggyApple", "沿用旧版动画行为", _settings.BuggyApple, v => _settings.BuggyApple = v);
        var buggy = _settings.BuggyAppleOptions;
        Check("ToggleSwitchAppleMusicLyrics_BuggyApple_BackgroundVocalsOverlap", "呼吸点后背景人声重叠", buggy.BackgroundVocalsOverlap, v => buggy.BackgroundVocalsOverlap = v);
        Check("ToggleSwitchAppleMusicLyrics_BuggyApple_BackgroundVocalsEnteringDirection", "背景人声进入方向问题", buggy.BackgroundVocalsEnteringDirection, v => buggy.BackgroundVocalsEnteringDirection = v);
        Check("ToggleSwitchAppleMusicLyrics_BuggyApple_TremendousBreathingDots", "过大的呼吸点", buggy.TremendousBreathingDots, v => buggy.TremendousBreathingDots = v);
        Check("ToggleSwitchAppleMusicLyrics_BuggyApple_BackgroundVocalTransparencySuddenChange", "背景人声行透明度突变", buggy.BackgroundVocalTransparencySuddenChange, v => buggy.BackgroundVocalTransparencySuddenChange = v);
        Check("ToggleSwitchAppleMusicLyrics_BuggyApple_ChineseCharacterAbnormalFloating", "中文歌词异常上浮", buggy.ChineseCharacterAbnormalFloating, v => buggy.ChineseCharacterAbnormalFloating = v);
        Check("ToggleSwitchAppleMusicLyrics_BuggyApple_LyricsLineTransparencySuddenChange", "歌词行透明度突变", buggy.LyricsLineTransparencySuddenChange, v => buggy.LyricsLineTransparencySuddenChange = v);
        Check("ToggleSwitchAppleMusicLyrics_BuggyApple_WordShineAnimationInterruption", "行尾发光动画中断", buggy.WordShineAnimationInterruption, v => buggy.WordShineAnimationInterruption = v);
        Check("ToggleSwitchAppleMusicLyricsPlusLighter", "高级混合", _settings.EnablePlusLighter, v => _settings.EnablePlusLighter = v);
        Choice("ComboBoxAppleMusicLyricsPlusLighterOverflowHandling", "混合预设", ["iOS 26", "iOS 18", "Android"], _settings.PlusLighterEffectPreset switch
        {
            PlusLighterEffectPresetTypes.iOS26 => 0,
            PlusLighterEffectPresetTypes.Android => 2,
            _ => 1
        }, i => _settings.PlusLighterEffectPreset = i switch { 0 => PlusLighterEffectPresetTypes.iOS26, 2 => PlusLighterEffectPresetTypes.Android, _ => PlusLighterEffectPresetTypes.iOS18 });
        var traditional = Check("ToggleSwitchEnableAppleMusicLyricsTraditional", "传统歌词页入口", _settings.EnableTraditionalEntry, v => _settings.EnableTraditionalEntry = v);
        traditional.IsEnabled = false;
        traditional.ToolTip = "独立工程提供当前歌词页；传统页入口属于原客户端。";
        Heading("翻译与歌词来源");
        Choice("HostLyricsParser", "歌词解析方式（下次载入生效）", ["内置解析器", "Cliric 扩展解析器"],
            options.UseCliricExtendedParser ? 1 : 0, i => options.UseCliricExtendedParser = i == 1);
        Check("HostTranslation", "显示翻译", options.Translation, v => options.Translation = v);
        Choice("ComboBoxLyricsRomajiMode", "音译显示", ["禁用", "翻译优先", "罗马音优先", "仅罗马音"], (int)options.RomajiOption, v => options.RomajiOption = (RomajiOptions)v);
        Check("HostEnableMultiLine", "允许同时高亮多行", options.EnableMultiLine, v => options.EnableMultiLine = v);
        Check("ToggleSwitchEnableColorfulEmoji", "翻译中的彩色 Emoji", options.EnableColorfulEmoji, v => options.EnableColorfulEmoji = v);
        Heading("窗口与封面");
        Check("HostReduceMotion", "减少动态效果", options.ReduceMotion, v => options.ReduceMotion = v);
        Check("HostTopmost", "窗口置顶", options.Topmost, v => options.Topmost = v);
        Check("HostLightTheme", "明亮背景", options.LightTheme, v => options.LightTheme = v, true);
        _audioReactive = Check("HostAudioReactive", "背景随系统音频律动", options.AudioReactive, v => options.AudioReactive = v, true);
        Children.Add(_backgroundAudioNote);
        Check("HostMarquee", "曲目信息滚动", _settings.EnableTrackInfoMarquee, v => _settings.EnableTrackInfoMarquee = v);
        Check("HostArtworkEnter", "封面进入动画", _settings.AnimationArtworkEnter, v => _settings.AnimationArtworkEnter = v);
        Check("HostDynamicArtwork", "动态封面视频", _settings.EnableDynamicArtwork, v => _settings.EnableDynamicArtwork = v);
        Check("HostChineseEasing", "中文扫光缓动", _settings.KaraokeChineseRollingEasing, v => _settings.KaraokeChineseRollingEasing = v);
        Slider("HostTimelineOffset", "歌词提前量 / ms", -1000, 1000, options.TimelineOffsetMs - LyricsPreparation.TimelineCompensationMs, 0, v => options.TimelineOffsetMs = v + LyricsPreparation.TimelineCompensationMs);
        Slider("HostAnimationSpeed", "动画时长倍率", .25, 3, options.AnimationSpeed, 2, v => options.AnimationSpeed = v);
        Slider("HostResume", "手动滚动恢复 / 秒", 1, 30, options.AutoResumeSeconds, 1, v => options.AutoResumeSeconds = v);
        var advanced = new StackPanel();
        var editor = new TextBox { Text = JsonSerializer.Serialize(_settings, LyricsFile.JsonOptions), AcceptsReturn = true, AcceptsTab = true, Height = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 8, 0, 4) };
        var error = new TextBlock { Foreground = Brushes.Salmon, TextWrapping = TextWrapping.Wrap };
        advanced.Children.Add(editor);
        advanced.Children.Add(error);
        advanced.Children.Add(Button("HostApplyAdvanced", "应用高级配置", () =>
        {
            try
            {
                var candidate = JsonSerializer.Deserialize<AppleMusicSettings>(editor.Text, LyricsFile.JsonOptions) ?? throw new JsonException("配置为空。");
                var validated = new ViewOptions { AppleMusicLyrics = candidate };
                validated.Normalize();
                _options.AppleMusicLyrics = candidate;
                _changed(true);
                reload?.Invoke();
            }
            catch (JsonException exception) { error.Text = exception.Message; }
        }));
        var advancedExpander = new Expander { Header = "高级歌词配置", Foreground = Brushes.White, Content = advanced, Margin = new Thickness(0, 15, 0, 5) };
        advancedExpander.Expanded += (_, _) => editor.Text = JsonSerializer.Serialize(_options.AppleMusicLyrics, LyricsFile.JsonOptions);
        Children.Add(advancedExpander);
        Children.Add(Button("HostReset", "恢复默认设置", reset));
        UpdateConditionalVisibility();
    }
    private void Heading(string text) => Children.Add(new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 19, Margin = new Thickness(0, 18, 0, 10) });
    private CheckBox Check(string name, string label, bool value, Action<bool> set, bool background = false)
    {
        var box = new CheckBox { Name = name, Content = label, IsChecked = value, Foreground = Brushes.White, Margin = new Thickness(2, 7, 2, 7) };
        box.Click += (_, _) => { set(box.IsChecked == true); Change(background); };
        Children.Add(box);
        return box;
    }
    private ComboBox Choice(string name, string label, string[] choices, int selected, Action<int> set, bool background = false)
    {
        var combo = new ComboBox { Name = name, ItemsSource = choices, SelectedIndex = selected, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(combo, label);
        combo.Tag = AddSettingRow(label, combo);
        combo.SelectionChanged += (_, _) => { if (_updating || combo.SelectedIndex < 0) return; set(combo.SelectedIndex); Change(background); };
        return combo;
    }
    private Slider Slider(string name, string label, double min, double max, double current, int decimals, Action<double> set)
    {
        double step = Math.Pow(10, -decimals);
        var slider = new ShellSlider
        {
            Name = name, Minimum = min, Maximum = max, Value = current,
            IsSnapToTickEnabled = true, TickFrequency = step, SmallChange = step, LargeChange = 10 * step,
            VerticalAlignment = VerticalAlignment.Center
        };
        var editor = new TextBox
        {
            Name = name + "Value", Width = 64, Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(7, 4, 7, 4),
            VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right
        };
        AutomationProperties.SetName(slider, label);
        AutomationProperties.SetName(editor, label + "数值");
        var field = new DockPanel();
        DockPanel.SetDock(editor, Dock.Right);
        field.Children.Add(editor);
        field.Children.Add(slider);
        slider.Tag = AddSettingRow(label, field);
        slider.ValueChanged += (_, _) => { set(Math.Round(slider.Value, decimals)); Change(); };
        slider.AttachValueEditor(editor, decimals);
        return slider;
    }

    private Grid AddSettingRow(string label, FrameworkElement field)
    {
        var row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        row.Children.Add(new TextBlock
        {
            Text = label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0)
        });
        Grid.SetColumn(field, 1);
        row.Children.Add(field);
        Children.Add(row);
        return row;
    }
    private static Button Button(string name, string text, Action action)
    {
        var button = new Button { Name = name, Content = text, Padding = new Thickness(8), Margin = new Thickness(0, 5, 0, 5) };
        button.Click += (_, _) => action();
        return button;
    }
    private void Change(bool background = false)
    {
        _options.Normalize();
        UpdateConditionalVisibility();
        _changed(background);
    }
    private void RefreshStyle()
    {
        _updating = true;
        try
        {
            int[] selections = _settings.GetStyleSelections();
            for (int i = 0; i < _styleOptions.Count; i++)
                _styleOptions[i].SelectedIndex = selections[i];
            _style.SelectedIndex = _settings.GetStylePreset();
        }
        finally { _updating = false; }
    }
    private void UpdateConditionalVisibility()
    {
        bool ios = _settings.Background == BackgroundTypes.CliricModern ||
            _settings.Background == BackgroundTypes.AppleMusicOriginal && ReferenceAppleMusicBackground.Available;
        _audioReactive.IsEnabled = ios && !_options.ReduceMotion;
        _backgroundAudioNote.Text = _settings.Background switch
        {
            BackgroundTypes.Static => "静态使用封面模糊图，背景保持静止。",
            BackgroundTypes.Dynamic => "动态使用封面图层按固定轨迹移动，当前没有接入系统音频。",
            BackgroundTypes.DynamicBeating => "原版依赖曲目的节拍分析数据。当前音源没有提供该数据，此模式暂按「动态」轨迹播放。",
            BackgroundTypes.DynamicIsolation => "动态流动根据封面取色生成流动网格，当前没有接入系统音频。",
            BackgroundTypes.AppleMusicOriginal when !ios => "当前使用兼容背景。原版效果需要 4.4.2 渲染组件。",
            BackgroundTypes.AppleMusicOriginal => "使用本地 4.4.2 背景渲染组件。",
            _ => "使用随 Cliric 提供的现代动态背景。"
        };
        if (ios)
            _backgroundAudioNote.Text += _options.ReduceMotion ? "关闭减少动态效果后可启用系统音频律动。" : "支持系统音频低频律动。";
        foreach (var (key, element) in _conditional)
        {
            bool visible = key switch
            {
                "independent" => ios,
                "palette" => _settings.Background == BackgroundTypes.DynamicIsolation,
                "blur" => _settings.BlurLyrics,
                "font" => _settings.FontPreset == FontPresets.Custom,
                "fps" => _settings.ShowFpsOverlay,
                _ => true
            };
            element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (element.Tag is FrameworkElement heading)
                heading.Visibility = element.Visibility;
        }
    }
}
