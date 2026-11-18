using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace LyricReplica.App;

internal static class ShellPlaybackControls
{
    internal static void Initialize(Button previous, Button play, Button next)
    {
        SetButtonIcon(previous, 28);
        SetButtonIcon(play, 36);
        SetButtonIcon(next, 28);
        play.Width = play.Height = 46;
        play.Margin = new Thickness(10, 0, 10, 0);
        ((Image)play.Content).RenderTransform = new TranslateTransform(1.5, 0);
        ApplyPalette(previous, play, next, playing: false);

        static void SetButtonIcon(Button button, double size)
        {
            button.SetResourceReference(FrameworkElement.StyleProperty, "Shell.IconButton");
            ShellTransitions.SetPlaybackFeedback(button, true);
            button.Content = new Image { Width = size, Height = size };
        }
    }

    internal static void ApplyPalette(Button previous, Button play, Button next, bool playing)
    {
        SetGlyph(previous, "Apple.Music.PreviousButton");
        SetGlyph(next, "Apple.Music.NextButton");
        SetGlyph(play, PlayResource(playing));
    }

    internal static ImageSource SetPlaying(Button button, bool playing, bool animate, bool reducedMotion)
    {
        var glyph = (Image)button.Content;
        if (animate && glyph.IsVisible)
            ShellTransitions.Reveal(glyph, .48, 0, MotionTokens.PlaybackState, reducedMotion);
        ((TranslateTransform)glyph.RenderTransform).X = playing ? 0 : 1.5;
        string label = playing ? "暂停" : "播放";
        button.ToolTip = label;
        AutomationProperties.SetName(button, label);
        return SetGlyph(button, PlayResource(playing));
    }

    private static string PlayResource(bool playing) => playing ? "Apple.Music.PauseButton" : "Apple.Music.PlayButton";

    private static ImageSource SetGlyph(Button button, string resource)
    {
        var image = (Image)button.Content;
        var source = (ImageSource)button.FindResource(resource);
        if (SystemParameters.HighContrast)
        {
            var drawing = new DrawingGroup { OpacityMask = new ImageBrush(source) };
            drawing.Children.Add(new GeometryDrawing(SystemColors.ControlTextBrush, null,
                new RectangleGeometry(new Rect(0, 0, source.Width, source.Height))));
            drawing.Freeze();
            image.Source = new DrawingImage(drawing);
        }
        else
            image.SetResourceReference(Image.SourceProperty, resource);
        return source;
    }
}
