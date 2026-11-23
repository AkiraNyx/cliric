using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using LyricReplica.App;

namespace CapsulePlayerDemo;

public partial class MainWindow
{
    private enum DemoRepeat { Off, Queue, Track }

    private readonly HashSet<int> _favorites = [];
    private DemoRepeat _repeatMode;
    private ToggleButton? _toolAnchor;
    private FrameworkElement? _toolSection;

    private void UpdateToolLayout()
    {
        bool wide = PlaybackBar.HasSoundTool;
        CompactSound.Visibility = wide ? Visibility.Collapsed : Visibility.Visible;
        if (ReferenceEquals(_toolSection, SoundOptions) && _toolAnchor is { } oldAnchor)
        {
            var anchor = wide ? PlaybackBar.SoundTool : PlaybackBar.TrackStamp;
            if (anchor != oldAnchor)
            {
                oldAnchor.IsChecked = false;
                _toolAnchor = anchor;
                anchor.IsChecked = true;
            }
        }
        PlaceToolFlyout();
    }

    private void ToggleTool(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: FrameworkElement section } anchor)
            return;
        if (_toolAnchor == anchor)
            CloseToolFlyout();
        else
            OpenToolFlyout(anchor, section);
    }

    private void OpenToolFlyout(ToggleButton anchor, FrameworkElement section)
    {
        if (_toolAnchor is { } oldAnchor)
            oldAnchor.IsChecked = false;
        if (_toolSection is { } oldSection)
            oldSection.Visibility = Visibility.Collapsed;
        _toolAnchor = anchor;
        _toolSection = section;
        anchor.IsChecked = true;
        section.Visibility = Visibility.Visible;
        PlaceToolFlyout();
        bool reduced = ShellTransitions.GetReducedMotion(this);
        if (ToolFlyout.Visibility != Visibility.Visible)
            ShellTransitions.PrepareReveal(ToolFlyout, MotionTokens.PopupStartOpacity, MotionTokens.PopupTravel);
        ToolFlyout.Visibility = Visibility.Visible;
        ToolFlyout.IsHitTestVisible = true;
        ShellTransitions.PlayReveal(ToolFlyout, MotionTokens.PopupEnter, reduced);
    }

    private void PlaceToolFlyout()
    {
        if (_toolAnchor is not { } anchor)
            return;
        const double inset = 16;
        double rightAligned = Math.Max(inset, Root.ActualWidth - ToolFlyout.Width - inset);
        double left = anchor == PlaybackBar.TrackStamp
            ? Math.Clamp(PlaybackBar.TrackStamp.TranslatePoint(new Point(), Root).X, inset, rightAligned)
            : rightAligned;
        ToolFlyout.Margin = new Thickness(left, 0, 0, 78);
    }

    private void CloseToolFlyout()
    {
        if (_toolAnchor is not { } anchor)
            return;
        anchor.IsChecked = false;
        _toolAnchor = null;
        ToolFlyout.IsHitTestVisible = false;
        bool reduced = ShellTransitions.GetReducedMotion(this);
        if (ToolFlyout.RenderTransform is TranslateTransform shift)
            ShellTransitions.To(shift, TranslateTransform.YProperty, MotionTokens.PopupTravel, MotionTokens.PopupExit, reduced);
        ShellTransitions.To(ToolFlyout, OpacityProperty, 0, MotionTokens.PopupExit, reduced, completed: () =>
        {
            ToolFlyout.Visibility = Visibility.Collapsed;
            _toolSection!.Visibility = Visibility.Collapsed;
            _toolSection = null;
        });
    }

    private void ChangeVolume(object sender, RoutedPropertyChangedEventArgs<double> e) => _audio.Volume = e.NewValue / 100;

    private void OpenCompactSound(object sender, RoutedEventArgs e) => OpenToolFlyout(PlaybackBar.TrackStamp, SoundOptions);

    private void ChangeRepeat(object sender, RoutedEventArgs e)
    {
        _repeatMode = _repeatMode switch { DemoRepeat.Off => DemoRepeat.Queue, DemoRepeat.Queue => DemoRepeat.Track, _ => DemoRepeat.Off };
        RepeatModeButton.Content = _repeatMode switch { DemoRepeat.Queue => "列表循环", DemoRepeat.Track => "单曲循环", _ => "循环关闭" };
    }

    private void ChangeShuffle(object sender, RoutedEventArgs e)
    {
        var selected = (DemoTrack)Tracks.SelectedItem;
        var order = _tracks;
        if (ShuffleMode.IsChecked == true)
        {
            order = (DemoTrack[])_tracks.Clone();
            Random.Shared.Shuffle(order);
            int index = Array.IndexOf(order, selected);
            (order[0], order[index]) = (order[index], order[0]);
        }
        QueueTracks.ItemsSource = order;
        QueueTracks.SelectedItem = selected;
    }

    private void SelectQueueTrack(object sender, SelectionChangedEventArgs e)
    {
        if (QueueTracks.SelectedItem is DemoTrack track && !ReferenceEquals(Tracks.SelectedItem, track))
            Tracks.SelectedItem = track;
    }

    private void ChangeFavorite(object sender, RoutedEventArgs e)
    {
        if (!_favorites.Add(Tracks.SelectedIndex))
            _favorites.Remove(Tracks.SelectedIndex);
        UpdateFavorite();
    }

    private void UpdateFavorite()
    {
        bool favorite = _favorites.Contains(Tracks.SelectedIndex);
        FavoriteToggle.IsChecked = favorite;
        FavoriteToggle.Content = favorite ? "取消收藏" : "收藏";
        PlaybackBar.FavoriteMark.Visibility = favorite ? Visibility.Visible : Visibility.Collapsed;
        CopyTitleButton.Content = "复制曲名";
    }

    private void CopyTitle(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetDataObject(PlaybackBar.CapsuleTitle.Text, true);
            CloseToolFlyout();
        }
        catch (ExternalException)
        {
            CopyTitleButton.Content = "复制失败，重试";
        }
    }
}
