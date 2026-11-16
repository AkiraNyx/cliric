using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LyricReplica.Core;

namespace LyricReplica.App;

public sealed partial class PlayerWindow : Window
{
    private const int CoverShelfLimit = 6;
    private readonly MainWindow _lyrics;
    private readonly bool _selfTest;
    private readonly DispatcherTimer _refresh = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(70) };
    private int _trackCount = -1, _activeTrack = -1;
    private bool _stoppingPlayback;
    private Song? _displayedSong;
    private readonly Dictionary<string, ImageSource> _shelfArtwork = [];
    private sealed record CoverTile(Song Track, ImageSource? Artwork);

    internal PlayerWindow(MainWindow lyrics, bool selfTest)
    {
        _lyrics = lyrics;
        _selfTest = selfTest;
        InitializeComponent();
        TrackList.ItemsSource = lyrics.Playlist;
        Progress.Grid.Opacity = 1;
        Progress.BorderPlayed.Background = (Brush)FindResource("Accent");
        Progress.SeekRequested += lyrics.Seek;
        _refresh.Tick += (_, _) =>
        {
            _lyrics.UpdatePlaybackWhileLyricsHidden();
            RefreshPlayer();
        };
        _lyrics.Closing += HideLyrics;
        Loaded += OnLoaded;
        Closing += StopPlaybackOnClose;
        Closed += (_, _) => { _refresh.Stop(); _lyrics.Closing -= HideLyrics; };
    }

    private async void StopPlaybackOnClose(object? sender, CancelEventArgs e)
    {
        if (!_lyrics.HasNetEasePlayback)
            return;
        e.Cancel = true;
        if (_stoppingPlayback)
            return;
        _stoppingPlayback = true;
        PlayerContent.IsEnabled = false;
        try
        {
            await _lyrics.StopNetEaseAsync();
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "网易云音乐", MessageBoxButton.OK, MessageBoxImage.Warning);
            PlayerContent.IsEnabled = true;
        }
        finally { _stoppingPlayback = false; }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _lyrics.StartPlaybackAsync();
        if (!IsVisible)
            return;
        PlayerContent.IsEnabled = true;
        RefreshPlayer();
        _refresh.Start();
        if (_selfTest)
            await VerifyPlayerAsync();
    }

    private void RefreshPlayer()
    {
        var song = _lyrics.CurrentSong;
        TrackTitle.Text = song.Title;
        TrackArtist.Text = _lyrics.TrackArtist;
        SourceLabel.Text = _lyrics.PlaybackSource;
        var artwork = _lyrics.TrackArtwork;
        bool artworkChanged = !ReferenceEquals(Cover.ImageSource, artwork);
        if (artworkChanged)
        {
            Cover.ImageSource = artwork;
            if (_lyrics.TrackIndex < CoverShelfLimit && artwork is not null)
                _shelfArtwork[song.Id] = artwork;
        }
        Notice.Text = _lyrics.PlaybackNotice;
        Notice.Visibility = Notice.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        string lyricsAction = _lyrics.IsVisible ? "切换到 Apple Music 歌词" : "打开 Apple Music 歌词";
        LyricsButton.ToolTip = lyricsAction;
        AutomationProperties.SetName(LyricsButton, lyricsAction);
        bool playing = _lyrics.Playing;
        string playLabel = playing ? "暂停" : "播放";
        if (AutomationProperties.GetName(PlayButton) != playLabel)
        {
            PlayGlyph.SetResourceReference(Image.SourceProperty, playing ? "Apple.Music.PauseButton" : "Apple.Music.PlayButton");
            PlayButton.ToolTip = playLabel;
            AutomationProperties.SetName(PlayButton, playLabel);
        }
        string favoriteLabel = _lyrics.IsHearted ? "已收藏" : "收藏";
        if (AutomationProperties.GetName(FavoriteButton) != favoriteLabel)
        {
            FavoriteGlyph.Text = _lyrics.IsHearted ? "" : "";
            FavoriteGlyph.Foreground = _lyrics.IsHearted ? (Brush)FindResource("Highlight") : Foreground;
            FavoriteButton.ToolTip = favoriteLabel;
            AutomationProperties.SetName(FavoriteButton, favoriteLabel);
        }
        ShuffleButton.Tag = _lyrics.ShuffleActive;
        RepeatButton.Tag = _lyrics.RepeatActive;
        AutomationProperties.SetName(ShuffleButton, _lyrics.ShuffleActive ? "随机播放，已开启" : "随机播放");
        AutomationProperties.SetName(RepeatButton, _lyrics.RepeatActive ? "循环播放，已开启" : "循环播放");
        ShuffleButton.IsEnabled = _lyrics.CanShuffle;
        RepeatButton.IsEnabled = _lyrics.CanRepeat;

        bool playlistChanged = _trackCount != _lyrics.Playlist.Count ||
            (_activeTrack == _lyrics.TrackIndex && !ReferenceEquals(_displayedSong, song));
        _displayedSong = song;
        if (playlistChanged)
        {
            _trackCount = _lyrics.Playlist.Count;
            TrackList.Items.Refresh();
            PlaylistCount.Text = $"{_trackCount} 首曲目";
            _activeTrack = -1;
        }
        if (_activeTrack != _lyrics.TrackIndex)
        {
            _activeTrack = _lyrics.TrackIndex;
            TrackList.SelectedIndex = _activeTrack;
            TrackList.ScrollIntoView(TrackList.SelectedItem);
            Progress.CancelPreview();
        }
        if (playlistChanged || (artworkChanged && _activeTrack < CoverShelfLimit))
        {
            CoverShelf.ItemsSource = _lyrics.Playlist.Take(CoverShelfLimit).Select((track, index) =>
            {
                if (!_shelfArtwork.TryGetValue(track.Id, out var thumbnail) && track.Id.StartsWith("demo-", StringComparison.Ordinal))
                    thumbnail = _shelfArtwork[track.Id] = Artwork.Create(index);
                return new CoverTile(track, thumbnail);
            }).ToArray();
        }
        Progress.IsRealTimeUpdate = _lyrics.Options.RealtimeSeek;
        Progress.ReduceMotion = _lyrics.Options.ReduceMotion;
        if (Progress.AnimatedTime != _lyrics.Options.AnimatedTime)
            Progress.AnimatedTime = _lyrics.Options.AnimatedTime;
        Progress.SetPosition(_lyrics.Clock.PositionMs, _lyrics.Clock.DurationMs);
    }

    private void OpenLyricsPage(object sender, RoutedEventArgs e)
    {
        if (!_lyrics.IsVisible)
            _lyrics.Show();
        if (_lyrics.WindowState == WindowState.Minimized)
            _lyrics.WindowState = WindowState.Normal;
        _lyrics.Activate();
        RefreshPlayer();
    }

    private void HideLyrics(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        _lyrics.Hide();
        RefreshPlayer();
    }

    private void OpenPlayerSettings(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(this, "播放器设置", 430, 310);
        var volumeLabel = new TextBlock { Margin = new Thickness(0, 0, 0, 8) };
        var volume = new Slider { Minimum = 0, Maximum = 100, Value = _lyrics.Options.Volume * 100, TickFrequency = 1, IsSnapToTickEnabled = true };
        AutomationProperties.SetName(volume, "本地音频音量");
        volumeLabel.Text = $"本地音频音量 {volume.Value:0}%";
        volume.ValueChanged += (_, _) => volumeLabel.Text = $"本地音频音量 {volume.Value:0}%";
        var realtimeSeek = new CheckBox { Content = "拖动进度时实时提交", IsChecked = _lyrics.Options.RealtimeSeek, Foreground = Foreground, Margin = new Thickness(0, 20, 0, 0) };
        var body = new StackPanel();
        body.Children.Add(volumeLabel);
        body.Children.Add(volume);
        body.Children.Add(realtimeSeek);
        dialog.SettingsContent = body;
        if (dialog.ShowDialog() == true)
            _lyrics.ApplyPlayerSettings(volume.Value / 100, realtimeSeek.IsChecked == true);
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e) =>
        CoverShelf.Tag = Math.Clamp((int)(e.NewSize.Width / 200), 4, CoverShelfLimit);

    private void OpenTrackActions(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    private async void PlaySelectedTrack(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(TrackList, source) is ListBoxItem)
            await SwitchTrackAsync();
    }

    private async void OnTrackListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        e.Handled = true;
        await SwitchTrackAsync();
    }

    private async Task SwitchTrackAsync()
    {
        if (TrackList.SelectedIndex >= 0)
        {
            try
            {
                await _lyrics.NavigateLocalAsync(TrackList.SelectedIndex);
                RefreshPlayer();
            }
            catch (Exception exception)
            {
                Notice.Text = exception.Message;
                Notice.Visibility = Visibility.Visible;
            }
        }
    }

    private async void OpenCoverTrack(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Song song })
        {
            TrackList.SelectedItem = song;
            await SwitchTrackAsync();
        }
    }

    private void OpenAudio(object sender, RoutedEventArgs e) => _lyrics.OpenAudio(this, newTrack: true);
    private void AttachAudio(object sender, RoutedEventArgs e) => _lyrics.OpenAudio(this);
    private void ImportLyrics(object sender, RoutedEventArgs e) => _lyrics.OpenLyrics(this);
    private void OpenCover(object sender, RoutedEventArgs e) => _lyrics.OpenCover(this);
    private void ConnectMedia(object sender, RoutedEventArgs e) => _lyrics.ConnectSystemMedia();
    private void OpenLyricsSettings(object sender, RoutedEventArgs e) => _lyrics.OpenLyricsSettings(this);
    private void PreviousTrack(object sender, RoutedEventArgs e) => _lyrics.Previous();
    private void TogglePlayback(object sender, RoutedEventArgs e) => _lyrics.TogglePlay();
    private void NextTrack(object sender, RoutedEventArgs e) => _lyrics.Next();
    private void ToggleShuffle(object sender, RoutedEventArgs e) => _lyrics.ToggleShuffle();
    private void ToggleRepeat(object sender, RoutedEventArgs e) => _lyrics.ToggleRepeat();
    private void ToggleFavorite(object sender, RoutedEventArgs e) => _lyrics.Favorite();
}
