using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;

namespace LyricReplica.App;

public sealed class MiSansLabel : Control
{
    private const FrameworkPropertyMetadataOptions TextLayout = FrameworkPropertyMetadataOptions.AffectsMeasure |
        FrameworkPropertyMetadataOptions.AffectsRender;

    public static readonly DependencyProperty TextProperty = TextBlock.TextProperty.AddOwner(typeof(MiSansLabel),
        new FrameworkPropertyMetadata("", TextLayout));
    public static readonly DependencyProperty TextWrappingProperty = TextBlock.TextWrappingProperty.AddOwner(typeof(MiSansLabel),
        new FrameworkPropertyMetadata(TextWrapping.NoWrap, TextLayout));
    public static readonly DependencyProperty TextTrimmingProperty = TextBlock.TextTrimmingProperty.AddOwner(typeof(MiSansLabel),
        new FrameworkPropertyMetadata(TextTrimming.None, TextLayout));
    public static readonly DependencyProperty UseDirectWriteProperty = DependencyProperty.Register(nameof(UseDirectWrite), typeof(bool),
        typeof(MiSansLabel), new FrameworkPropertyMetadata(false, TextLayout));

    static MiSansLabel()
    {
        FontSizeProperty.OverrideMetadata(typeof(MiSansLabel), new FrameworkPropertyMetadata(SystemFonts.MessageFontSize, TextLayout));
        FontWeightProperty.OverrideMetadata(typeof(MiSansLabel), new FrameworkPropertyMetadata(FontWeights.Normal, TextLayout));
        ForegroundProperty.OverrideMetadata(typeof(MiSansLabel), new FrameworkPropertyMetadata(SystemColors.ControlTextBrush, TextLayout));
    }

    public string Text { get => (string?)GetValue(TextProperty) ?? ""; set => SetValue(TextProperty, value); }
    public TextWrapping TextWrapping { get => (TextWrapping)GetValue(TextWrappingProperty); set => SetValue(TextWrappingProperty, value); }
    public TextTrimming TextTrimming { get => (TextTrimming)GetValue(TextTrimmingProperty); set => SetValue(TextTrimmingProperty, value); }
    public bool UseDirectWrite { get => (bool)GetValue(UseDirectWriteProperty); set => SetValue(UseDirectWriteProperty, value); }

    private int Weight => FontWeight.ToOpenTypeWeight();
    private int Features => MiSansTypography.GetFeatures(this);
    private bool UseNative => Foreground is SolidColorBrush && (UseDirectWrite || !MiSansTypography.IsNamedWeight(Weight) || Features != 0);

    protected override Size MeasureOverride(Size constraint)
    {
        if (GetTemplateChild("Text") is TextBlock label)
            label.Visibility = UseNative ? Visibility.Collapsed : Visibility.Visible;
        if (!UseNative)
            return base.MeasureOverride(constraint);
        return string.IsNullOrEmpty(Text) ? new Size() :
            MiSansRenderer.Measure(Text, FontSize, Weight, Features, constraint, TextWrapping, TextTrimming);
    }

    protected override void OnRender(DrawingContext context)
    {
        if (!UseNative || string.IsNullOrEmpty(Text) || RenderSize.Width <= 0 || RenderSize.Height <= 0 ||
            Foreground is not SolidColorBrush brush)
            return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var color = brush.Color;
        color.A = (byte)Math.Round(color.A * brush.Opacity);
        var image = MiSansRenderer.Render(Text, FontSize, Weight, Features, RenderSize, dpi, color, TextWrapping, TextTrimming);
        context.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        context.DrawImage(image, new Rect(0, 0, image.Width, image.Height));
        context.Pop();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (UseNative)
            InvalidateVisual();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new LabelPeer(this);

    private sealed class LabelPeer(MiSansLabel label) : FrameworkElementAutomationPeer(label)
    {
        protected override string GetNameCore() => AutomationProperties.GetName(label) is { Length: > 0 } name ? name : label.Text;
        protected override string GetClassNameCore() => nameof(MiSansLabel);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
        protected override List<AutomationPeer>? GetChildrenCore() => null;
    }
}
