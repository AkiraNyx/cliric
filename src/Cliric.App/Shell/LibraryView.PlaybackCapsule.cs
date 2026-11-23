using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using LiquidGlass;
using LiquidGlass.Wpf;
using Cliric.App.Reference;

namespace Cliric.App;

public sealed partial class LibraryView
{
    private readonly GlassScene _playbackScene;
    private ToggleButton? _soundAnchor;

    private Button PreviousButton => PlaybackBar.PreviousButton;
    private Button PlayButton => PlaybackBar.PlayButton;
    private Button NextButton => PlaybackBar.NextButton;
    private ToggleButton QueueButton => PlaybackBar.QueueTool;
    private ToggleButton LyricsButton => PlaybackBar.LyricsTool;
    private ReferenceStateBar Progress => PlaybackBar.Seek;

    private GlassScene CreatePlaybackScene()
    {
        LibraryRoot.Children.Remove(PlaybackBar);
        LibraryRoot.Children.Remove(SoundSurface);
        Content = null;
        var scene = new GlassScene { Content = LibraryRoot, ClipToBounds = true };
        GlassScene.SetAppearance(scene, GlassAppearance.Dark);
        scene.Children.Add(PlaybackBar);
        scene.Children.Add(SoundSurface);
        Content = scene;

        PlaybackBar.InitializeControls();
        PreviousButton.Click += PreviousTrack;
        PlayButton.Click += TogglePlayback;
        NextButton.Click += NextTrack;
        PlaybackBar.TrackStamp.Click += OpenTrackActions;
        PlaybackBar.SoundTool.Click += ToggleSound;
        QueueButton.Click += ToggleQueuePopup;
        LyricsButton.Click += OpenLyricsPage;
        ToolTipService.SetShowOnDisabled(LyricsButton, true);
        QueuePopup.PlacementTarget = QueueButton;
        return scene;
    }

    private void ToggleSound(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(_soundAnchor, PlaybackBar.SoundTool))
            CloseSound(animate: true);
        else
            ShowSound(PlaybackBar.SoundTool);
    }

    private void ShowSound(ToggleButton anchor)
    {
        CloseAccount(animate: false);
        CloseQueue(animate: false);
        if (_shellMenu is { } menu)
            menu.IsOpen = false;
        if (_soundAnchor is { } oldAnchor)
            oldAnchor.IsChecked = false;
        _soundAnchor = anchor;
        anchor.IsChecked = true;

        const double inset = 16;
        double rightAligned = Math.Max(inset, ActualWidth - SoundSurface.Width - inset);
        double left = anchor == PlaybackBar.TrackStamp
            ? Math.Clamp(anchor.TranslatePoint(new Point(), this).X, inset, rightAligned)
            : rightAligned;
        SoundSurface.Margin = new Thickness(left, 0, 0, 78);
        if (SoundSurface.Visibility == Visibility.Collapsed)
            ShellTransitions.PrepareReveal(SoundSurface, MotionTokens.PopupStartOpacity, MotionTokens.PopupTravel);
        SoundSurface.Visibility = Visibility.Visible;
        SoundSurface.IsHitTestVisible = true;
        ShellTransitions.PlayReveal(SoundSurface, MotionTokens.PopupEnter, MotionReduced);
    }

    private void CloseSound(bool animate)
    {
        if (_soundAnchor is { } anchor)
            anchor.IsChecked = false;
        else if (animate || SoundSurface.Visibility == Visibility.Collapsed)
            return;

        _soundAnchor = null;
        SoundSurface.IsHitTestVisible = false;
        if (_shellMenu?.PlacementTarget == NetEaseQualityButton)
            _shellMenu.IsOpen = false;
        if (SoundSurface.RenderTransform is TranslateTransform shift)
            ShellTransitions.To(shift, TranslateTransform.YProperty, MotionTokens.PopupTravel, MotionTokens.PopupExit,
                immediate: !animate || MotionReduced);
        ShellTransitions.To(SoundSurface, OpacityProperty, 0, MotionTokens.PopupExit,
            immediate: !animate || MotionReduced, completed: () =>
            {
                if (_soundAnchor is null)
                    SoundSurface.Visibility = Visibility.Collapsed;
            });
    }
}
