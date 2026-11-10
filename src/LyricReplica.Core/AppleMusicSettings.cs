using System.Text.Json.Serialization;

namespace LyricReplica.Core;

// Settings keys, numeric enums and defaults come from the fixed 4.4.2 release contract.
public enum BackgroundTypes { Static = 0, Dynamic = 1, DynamicBeating = 2, DynamicIsolation = 3, AppleMusicOriginal = 4 }
public enum IsolationPalettes { Auto = 0, KMeans = 1, OctTree = 2, MedianCut1 = 3, MedianCut2 = 4 }
public enum FontPresets { Default = 0, SpotifyMix = 1, SpotifyCircular = 2, SFPro = 3, Custom = 4 }
public enum WordShineEffectPresetTypes { Lyricify = 0, Android = 1, iOS162 = 2, iOS264 = 3 }
public enum InterLineAnimationPresetTypes { iOS = 0, Lyricify = 1 }
public enum TranslationLinePresetTypes { Lyricify = 0, iOS = 1 }
public enum PlusLighterEffectPresetTypes { iOS18 = 0, iOS = 0, Android = 1, iOS26 = 2 }

[JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
public sealed partial class AppleMusicSettings
{
    private WordShineEffectPresetTypes _wordShine=WordShineEffectPresetTypes.iOS162;
    private InterLineAnimationPresetTypes _interLine;
    private TranslationLinePresetTypes _translationLine;
    [JsonPropertyName("blur_lyrics")] public bool BlurLyrics { get; set; } = false;
    [JsonPropertyName("blur_level")] public double BlurLevel { get; set; } = 0.6;
    [JsonPropertyName("animation_speed")] public double AnimationSpeed { get; set; } = 1.0;
    [JsonPropertyName("enable_track_info_marquee")] public bool EnableTrackInfoMarquee { get; set; } = true;
    [JsonPropertyName("animation_artwork_enter")] public bool AnimationArtworkEnter { get; set; } = true;
    [JsonPropertyName("pulling")] public bool PullingEffect { get; set; } = true;
    [JsonPropertyName("pulling_new")] public bool PullingEffectNew { get; set; } = true;
    [JsonPropertyName("pulling_delay")] public double PullingDelay { get; set; } = 2.5;
    [JsonPropertyName("background_type")] public BackgroundTypes Background { get; set; } = BackgroundTypes.Static;
    [JsonPropertyName("background_dynamic_frame_rate")] public int BackgroundDynamicFrameRate { get; set; } = 12;
    [JsonPropertyName("background_dynamic_isolation_palettes_beta_2")] public IsolationPalettes BackgroundDynamicIsolationPalettes { get; set; } = IsolationPalettes.Auto;
    [JsonPropertyName("background_dynamic_isolation_frame_rate_limit")] public int? BackgroundDynamicIsolationFrameRateLimit { get; set; } = -1;
    [JsonPropertyName("background_dynamic_isolation_render_scale")] public double BackgroundDynamicIsolationRenderScale { get; set; } = 0.5;
    [JsonPropertyName("enable_independent_background_renderer")] public bool EnableIndependentBackgroundRenderer { get; set; } = false;
    [JsonPropertyName("background_apple_music_original")] public AppleMusicOriginalBackgroundSettings BackgroundAppleMusicOriginal { get; set; } = new();
    [JsonPropertyName("bold_text")] public bool BoldText { get; set; } = false;
    [JsonPropertyName("font_preset")] public FontPresets FontPreset { get; set; } = FontPresets.Default;
    [JsonPropertyName("font_family")] public string FontFamily { get; set; } = "";
    [JsonPropertyName("is_karaoke")] public bool Karaoke { get; set; } = true;
    [JsonPropertyName("disable_cjk_karaoke")] public bool IsCjkKaraokeDisabled { get; set; } = false;
    [JsonPropertyName("karaoke_style_enable_animation")] public bool KaraokeStyleEnableAnimation { get; set; } = true;
    [JsonPropertyName("karaoke_style_float")] public bool KaraokeStyleFloat { get; set; } = true;
    [JsonPropertyName("karaoke_style_float_height")] public double KaraokeStyleFloatHeight { get; set; } = -1.0;
    [JsonPropertyName("karaoke_style_progress_width")] public double KaraokeStyleProgressWidth { get; set; } = double.NaN;
    [JsonPropertyName("karaoke_style_progress_width_preset")] public int KaraokeStyleProgressWidthPreset { get; set; } = 0;
    [JsonPropertyName("karaoke_style_word_shine")] public bool KaraokeStyleWordShine { get; set; } = true;
    [JsonPropertyName("karaoke_chinese_easing")] public bool KaraokeChineseRollingEasing { get; set; } = true;
    [JsonPropertyName("duet_view")] public bool DuetView { get; set; } = true;
    [JsonPropertyName("right_align_first")] public bool RightAlignFirst { get; set; } = false;
    [JsonPropertyName("magic_strip_tutorial_count")] public int MagicStripTutorialCount { get; set; } = 5;
    [JsonPropertyName("magic_strip_tutorial")] public bool? MagicStripTutorial { get; set; } = null;
    [JsonPropertyName("magic_strip_status")] public bool? magicStripStatus { get; set; } = null;
    [JsonIgnore] public bool MagicStripStatus { get => magicStripStatus ?? MagicStripTutorial ?? true; set => magicStripStatus=value; }
    [JsonPropertyName("enable_traditional_entry")] public bool EnableTraditionalEntry { get; set; } = false;
    [JsonPropertyName("enable_dynamic_artwork")] public bool EnableDynamicArtwork { get; set; } = true;
    [JsonPropertyName("unplaying_opacity_override")] public double UnplayingOpacityOverride { get; set; } = double.NaN;
    [JsonPropertyName("playing_zoom_override")] public double PlayingZoomOverride { get; set; } = double.NaN;
    [JsonPropertyName("buggy_apple")] public bool BuggyApple { get; set; } = false;
    [JsonPropertyName("buggy_apple_options")] public BuggyAppleOptions BuggyAppleOptions { get; set; } = new();
    [JsonPropertyName("shiny_cjk")] public bool ShinyCjk { get; set; } = false;
    [JsonPropertyName("word_shine_effect_preset")] public WordShineEffectPresetTypes WordShineEffectPreset { get => _wordShine; set => _wordShine=(uint)value<=3?value:WordShineEffectPresetTypes.iOS162; }
    [JsonPropertyName("word_shine_effect_ios")] public bool WordShineEffectIos { get; set; } = true;
    [JsonPropertyName("line_switching_apple_ios_style_16_4")] public bool LineSwitchingAppleIosStyle164 { get; set; } = true;
    [JsonPropertyName("inter_line_animation_preset")] public InterLineAnimationPresetTypes InterLineAnimationPreset { get => _interLine; set => _interLine=(uint)value<=1?value:InterLineAnimationPresetTypes.iOS; }
    [JsonPropertyName("line_switching_opacity_speed_apple_ios_style_16_4")] public bool LineSwitchingOpacitySpeedAppleIosStyle164 { get; set; } = false;
    [JsonPropertyName("current_line_preplay_opacity_preset")] public int CurrentLinePreplayOpacityPreset { get; set; } = 0;
    [JsonPropertyName("translation_line_preset")] public TranslationLinePresetTypes TranslationLinePreset { get => _translationLine; set => _translationLine=(uint)value<=1?value:TranslationLinePresetTypes.Lyricify; }
    [JsonPropertyName("background_vocals_enter_direction_left")] public bool BackgroundVocalsEnterDirectionLeft { get; set; } = false;
    [JsonPropertyName("background_vocals_native_position")] public bool BackgroundVocalsNativePosition { get; set; } = true;
    [JsonPropertyName("background_vocals_enter_animation")] public int BackgroundVocalsEnterAnimation { get; set; } = 1;
    [JsonIgnore] public bool BackgroundVocalsEnterAnimationNew => BackgroundVocalsEnterAnimation != 2;
    [JsonIgnore] public bool BackgroundVocalsEnterAnimationNewNew => BackgroundVocalsEnterAnimation == 0;
    [JsonPropertyName("ios_style_preset")] public int? IosStylePreset { get; set; } = null;
    [JsonPropertyName("breathing_dots_align_left")] public bool BreathingDotsAlignmentLeft { get; set; } = false;
    [JsonPropertyName("highlight_line_zooming_preset")] public int HighlightLineZooming { get; set; } = 0;
    [JsonPropertyName("enable_plus_lighter")] public bool EnablePlusLighter { get; set; } = false;
    [JsonPropertyName("plus_lighter_effect_preset")] public PlusLighterEffectPresetTypes PlusLighterEffectPreset { get; set; } = PlusLighterEffectPresetTypes.iOS18;
    [JsonPropertyName("prevent_sleep_fullscreen_only")] public bool PreventSleepFullscreenOnly { get; set; } = false;
    [JsonPropertyName("enable_advanced_button_drag_animation")] public bool EnableAdvancedButtonDragAnimation { get; set; } = true;
    [JsonPropertyName("enable_animated_time_status")] public bool EnableAnimatedTimeStatus { get; set; } = true;
    [JsonPropertyName("disable_blur_at_lyrics_line_mouse_over")] public bool DisableBlurAtLyricsLineMouseOver { get; set; } = false;
    [JsonPropertyName("zooming_restore")] public bool ZoomingRestore { get; set; } = true;
    [JsonPropertyName("zooming_levels")] public ZoomLevels ZoomingLevels { get; set; } = new();
    [JsonPropertyName("show_fps_overlay")] public bool ShowFpsOverlay { get; set; } = false;
    [JsonPropertyName("fps_overlay_update_interval")] public double FpsOverlayUpdateInterval { get; set; } = 0.5;
}

[JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
public sealed partial class AppleMusicOriginalBackgroundSettings
{
    [JsonPropertyName("frame_rate_limit")] public int? FrameRateLimit { get; set; } = -1;
    [JsonPropertyName("render_scale")] public double RenderScale { get; set; } = 1.0;
    [JsonPropertyName("bass_pulse_scale")] public double BassPulseScale { get; set; } = 1.0;
    [JsonPropertyName("blur_scale")] public double BlurScale { get; set; } = 1.0;
    [JsonPropertyName("portrait_control_point_count")] public int PortraitControlPointCount { get; set; } = -1;
    [JsonPropertyName("portrait_subdivision_levels")] public int PortraitSubdivisionLevels { get; set; } = -1;
    [JsonPropertyName("landscape_control_point_count")] public int LandscapeControlPointCount { get; set; } = -1;
    [JsonPropertyName("landscape_subdivision_levels")] public int LandscapeSubdivisionLevels { get; set; } = -1;
}

[JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
public sealed partial class BuggyAppleOptions
{
    [JsonPropertyName("background_vocals_overlap")] public bool BackgroundVocalsOverlap { get; set; } = true;
    [JsonPropertyName("background_vocals_entering_direction")] public bool BackgroundVocalsEnteringDirection { get; set; } = true;
    [JsonPropertyName("tremendous_breathing_dots")] public bool TremendousBreathingDots { get; set; } = false;
    [JsonPropertyName("background_vocal_transparency_sudden_change")] public bool BackgroundVocalTransparencySuddenChange { get; set; } = true;
    [JsonPropertyName("chinese_character_abnormal_floating")] public bool ChineseCharacterAbnormalFloating { get; set; } = true;
    [JsonPropertyName("lyrics_line_transparency_sudden_change")] public bool LyricsLineTransparencySuddenChange { get; set; } = false;
    [JsonPropertyName("word_shine_animation_interruption")] public bool WordShineAnimationInterruption { get; set; } = false;
}

[JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
public sealed partial class ZoomLevels
{
    [JsonPropertyName("horizontal")] public double Horizontal { get; set; } = double.NaN;
    [JsonPropertyName("vertical")] public double Vertical { get; set; } = double.NaN;
    [JsonPropertyName("horizontal_fullscreen")] public double HorizontalFullscreen { get; set; } = double.NaN;
    [JsonPropertyName("vertical_fullscreen")] public double VerticalFullscreen { get; set; } = double.NaN;
}
