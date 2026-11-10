namespace LyricReplica.Core;

public sealed partial class AppleMusicSettings
{
    public bool BeginMagicStripTutorial()
    {
        if (!MagicStripTutorial.HasValue)
        {
            if (MagicStripTutorialCount <= 0)
                MagicStripTutorial = false;
            else
                MagicStripTutorialCount--;
        }
        return MagicStripTutorial != false;
    }
    public static readonly string[] StylePresetNames = ["默认", "iOS 26.4 及以后", "iOS 26.0", "iOS 17.1", "iOS 16.4", "iOS 16.3", "iOS 16.2", "iOS 15", "Android（新版）", "Android（旧版）", "自定义"];
    private static readonly int[][] StylePresets =
    [
        [0,0,1,0,0,1,0,1,1,0,0,0],
        [0,0,0,1,0,0,1,0,0,1,0,1],
        [0,0,0,1,0,0,1,1,0,1,0,1],
        [0,0,0,1,1,1,1,1,0,1,0,1],
        [0,0,0,1,1,1,2,1,0,1,0,1],
        [1,1,1,1,1,1,2,1,0,1,0,1],
        [1,1,1,1,1,2,2,1,0,1,0,1],
        [1,1,1,1,1,2,2,1,0,2,0,1],
        [0,1,0,2,1,1,0,2,1,3,0,0],
        [2,1,0,2,1,1,0,2,1,3,0,0]
    ];
    public int[] GetStyleSelections() =>
    [
        KaraokeStyleProgressWidthPreset, LineSwitchingAppleIosStyle164?0:1,
        LineSwitchingOpacitySpeedAppleIosStyle164?0:1, CurrentLinePreplayOpacityPreset,
        BackgroundVocalsNativePosition?0:1,
        BackgroundVocalsEnterAnimation==0?0:BackgroundVocalsEnterAnimation==2?3:BackgroundVocalsEnterDirectionLeft?2:1,
        IosStylePreset==17?1:IosStylePreset==16?2:0,
        WordShineEffectPreset switch { WordShineEffectPresetTypes.iOS264=>0,WordShineEffectPresetTypes.Android=>2,WordShineEffectPresetTypes.Lyricify=>3,_=>1 },
        BreathingDotsAlignmentLeft?0:1, HighlightLineZooming, (int)InterLineAnimationPreset, (int)TranslationLinePreset
    ];
    public int GetStylePreset()
    {
        var selection = GetStyleSelections();
        for (int i = 0; i < StylePresets.Length; i++)
        if (selection.SequenceEqual(StylePresets[i]))
            return i;
        return 10;
    }
    public void ApplyStylePreset(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, 10);
        if (index == 10)
            return;
        for (int i = 0; i < 12; i++)
            SetStyleSelection(i, StylePresets[index][i]);
    }
    public void SetStyleSelection(int option, int selected)
    {
        int[] counts = [3, 2, 2, 3, 2, 4, 3, 4, 2, 4, 2, 2];
        if (option < 0 || option >= counts.Length || selected < 0 || selected >= counts[option])
            throw new ArgumentOutOfRangeException(nameof(selected));
        switch (option)
        {
            case 0:
                KaraokeStyleProgressWidthPreset = selected;
                break;
            case 1:
                LineSwitchingAppleIosStyle164 = selected == 0;
                break;
            case 2:
                LineSwitchingOpacitySpeedAppleIosStyle164 = selected == 0;
                break;
            case 3:
                CurrentLinePreplayOpacityPreset = selected;
                break;
            case 4:
                BackgroundVocalsNativePosition = selected == 0;
                break;
            case 5:
                BackgroundVocalsEnterDirectionLeft = selected == 2;
                BackgroundVocalsEnterAnimation = selected switch
                {
                    0 => 0,
                    3 => 2,
                    _ => 1
                };
                break;
            case 6:
                IosStylePreset = selected switch
                {
                    1 => 17,
                    2 => 16,
                    _ => (int?)null
                };
                break;
            case 7:
                WordShineEffectPreset = selected switch
                {
                    0 => WordShineEffectPresetTypes.iOS264,
                    2 => WordShineEffectPresetTypes.Android,
                    3 => WordShineEffectPresetTypes.Lyricify,
                    _ => WordShineEffectPresetTypes.iOS162
                };
                break;
            case 8:
                BreathingDotsAlignmentLeft = selected == 0;
                break;
            case 9:
                HighlightLineZooming = selected;
                break;
            case 10:
                InterLineAnimationPreset = (InterLineAnimationPresetTypes)selected;
                break;
            case 11:
                TranslationLinePreset = (TranslationLinePresetTypes)selected;
                break;
        }
    }
    public void SetPullingPower(int power)
    {
        if (power < 0 || power > 5)
            return;
        PullingEffect = power != 0;
        PullingDelay = new double[] { 0, 1.2, 1.8, 2.5, 3, 4 }[power];
    }
    public int GetPullingPower() => !PullingEffect ? 0 : Array.IndexOf(new double[] { 0, 1.2, 1.8, 2.5, 3, 4 }, PullingDelay);
    public void SetFloatSelection(int index)
    {
        KaraokeStyleFloat = index != 0;
        KaraokeStyleFloatHeight = index switch
        {
            1 => 2.5,
            2 => 4,
            4 => 8,
            _ => -1
        };
    }
    public int GetFloatSelection() => !KaraokeStyleFloat ? 0 : KaraokeStyleFloatHeight switch { 2.5 => 1, 4 => 2, 8 => 4, _ => 3 };
    public double GetFloatHeight() => KaraokeStyleFloatHeight > 0 ? Math.Min(KaraokeStyleFloatHeight, 10) / 2 : 2.75;
    public double GetProgressWidth(bool portrait) => .8 * (double.IsNaN(KaraokeStyleProgressWidth) ? KaraokeStyleProgressWidthPreset switch { 0 => portrait ? 32 : 25, 1 => portrait ? 23 : 18, 2 => portrait ? 90 : 80, _ => 25 } : KaraokeStyleProgressWidth);
    public byte GetPreplayOpacity() => CurrentLinePreplayOpacityPreset switch { 2 => 47, 1 => 95, _ => 71 };
    public double GetHighlightScale(bool timed)
    {
        double value = HighlightLineZooming switch
        {
            1 => 1.02,
            2 => 1.1,
            3 => 1.05,
            _ => timed ? 1.02 : BlurLyrics ? 1.1 : 1.05
        };
        return double.IsNormal(PlayingZoomOverride) && PlayingZoomOverride >= .5 && PlayingZoomOverride <= 1.5 ? PlayingZoomOverride : value;
    }
    public double GetSecondaryAlpha(bool timed, bool background)
    {
        double main = TranslationLinePreset == TranslationLinePresetTypes.iOS ? 95d / 255 : timed ? .7 : 1;
        return !background ? main : TranslationLinePreset == TranslationLinePresetTypes.iOS ? Math.Min(1, main / .66) : .5;
    }
}
