using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Cliric.App;

internal static class MiSansTypography
{
    internal const string FamilyName = "MiSans VF";
    internal const int MinimumWeight = 150, DefaultWeight = 330, MaximumWeight = 700;
    internal const int Superscript = 1 << 10, Subscript = 1 << 11, Tabular = 1 << 13;
    internal const int AllFeatures = (1 << 15) - 1;

    internal static readonly FontFamily Family = new(new Uri("pack://application:,,,/"),
        "./Assets/Fonts/#MiSans VF, Microsoft YaHei UI, Segoe UI");
    internal static readonly Uri FontResource = new("pack://application:,,,/Cliric;component/Assets/Fonts/MiSansVF.ttf");

    internal static readonly (string Tag, string Label, string Sample)[] Features =
    [
        ("ss01", "数字专用标点", "12:34, 7÷3"),
        ("ss02", "单位上标", "64GB 23℃"),
        ("ss03", "单位下标", "64GB 23℃"),
        ("ss04", "数字形态一", "0123456789"),
        ("ss05", "字母 M 形态一", "M"),
        ("ss06", "字母 M 形态二", "M"),
        ("ss07", "引号和逗号替代", "“MiSans”，‘音乐’"),
        ("ss08", "星号和井号替代", "* #"),
        ("ss09", "数字形态二", "0123456789"),
        ("frac", "自动分数", "1/2 3/4"),
        ("sups", "上标", "abc123"),
        ("subs", "下标", "H2O (123)"),
        ("case", "大写标点", "(AB) [CD]"),
        ("tnum", "等宽数字", "1111 8888"),
        ("vert", "竖排替代字形", "「音乐」（播放）、。")
    ];

    internal static readonly DependencyProperty FeaturesProperty = DependencyProperty.RegisterAttached(
        "Features", typeof(int), typeof(MiSansTypography), new FrameworkPropertyMetadata(0,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure |
            FrameworkPropertyMetadataOptions.AffectsRender));

    internal static int GetFeatures(DependencyObject item) => (int)item.GetValue(FeaturesProperty);

    internal static bool IsNamedWeight(int weight) =>
        weight is 150 or 200 or 250 or 305 or 330 or 380 or 450 or 520 or 630 or 700;

    internal static void Apply(DependencyObject target, int features)
    {
        target.SetValue(FeaturesProperty, features);
        Typography.SetStylisticSet1(target, Enabled(0));
        Typography.SetStylisticSet2(target, Enabled(1));
        Typography.SetStylisticSet3(target, Enabled(2));
        Typography.SetStylisticSet4(target, Enabled(3));
        Typography.SetStylisticSet5(target, Enabled(4));
        Typography.SetStylisticSet6(target, Enabled(5));
        Typography.SetStylisticSet7(target, Enabled(6));
        Typography.SetStylisticSet8(target, Enabled(7));
        Typography.SetStylisticSet9(target, Enabled(8));
        Typography.SetFraction(target, Enabled(9) ? FontFraction.Slashed : FontFraction.Normal);
        Typography.SetVariants(target, Enabled(10) ? FontVariants.Superscript : Enabled(11) ? FontVariants.Subscript : FontVariants.Normal);
        Typography.SetCaseSensitiveForms(target, Enabled(12));
        Typography.SetNumeralAlignment(target, Enabled(13) ? FontNumeralAlignment.Tabular : FontNumeralAlignment.Normal);

        bool Enabled(int index) => (features & (1 << index)) != 0;
    }
}
