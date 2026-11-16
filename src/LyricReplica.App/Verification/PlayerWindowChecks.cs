using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LyricReplica.Core;

namespace LyricReplica.App;

public sealed partial class PlayerWindow
{
    private async Task VerifyPlayerAsync()
    {
        if (_lyrics.IsVisible || TrackList.Items.Count != _lyrics.Playlist.Count || Cover.ImageSource is null ||
            CoverShelf.Items.Count != Math.Min(CoverShelfLimit, _lyrics.Playlist.Count))
            throw new InvalidOperationException("The player must open with its song and playlist before the lyric window appears.");

        var song = _lyrics.CurrentSong;
        var clock = _lyrics.Clock;
        double start = clock.PositionMs;
        PlayButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, PlayButton));
        await Task.Delay(100);
        RefreshPlayer();
        if (!clock.IsPlaying || clock.PositionMs <= start || AutomationProperties.GetName(PlayButton) != "暂停")
            throw new InvalidOperationException("The player must advance the shared clock while the lyrics are hidden.");

        LyricsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, LyricsButton));
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        if (!_lyrics.IsVisible || _lyrics.CurrentSong != song || _lyrics.Clock != clock || !clock.IsPlaying)
            throw new InvalidOperationException("Opening the lyric window must preserve the active track and playback clock.");

        double beforeHide = clock.PositionMs;
        _lyrics.Close();
        await Task.Delay(100);
        if (_lyrics.IsVisible || !IsVisible || !clock.IsPlaying || clock.PositionMs <= beforeHide)
            throw new InvalidOperationException("Closing lyrics must leave the player and its playback running.");

        PlayButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, PlayButton));
        RefreshPlayer();
        string folder = Path.Combine(App.DataFolder, "verification");
        Directory.CreateDirectory(folder);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        CapturePlayer(folder, "player.png");
        double width = Width, height = Height;
        Width = MinWidth;
        Height = MinHeight;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        CapturePlayer(folder, "player-compact.png");
        var detailsBounds = TrackDetails.TransformToAncestor(PlaybackBar).TransformBounds(new Rect(TrackDetails.RenderSize));
        if (TrackList.ActualHeight < 54 || Progress.ActualWidth < 160 || detailsBounds.Top < 0 || detailsBounds.Bottom > PlaybackBar.ActualHeight)
            throw new InvalidOperationException($"Compact player: queue height {TrackList.ActualHeight}, seek width {Progress.ActualWidth}, track details {detailsBounds} within {PlaybackBar.ActualHeight}.");
        Width = width;
        Height = height;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var secondSong = _lyrics.Playlist[1];
        var tile = WordEdgeChecks.Find(CoverShelf).OfType<Button>().Single(button => ReferenceEquals(button.Tag, secondSong));
        tile.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, tile));
        if (_lyrics.CurrentSong != secondSong)
            throw new InvalidOperationException("A cover tile must select the same track as its queue row.");
        await _lyrics.LoadSongAsync(0);
        LyricsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, LyricsButton));
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        if (!_lyrics.IsVisible || _lyrics.Clock != clock || clock.IsPlaying || _lyrics.CurrentSong != song)
            throw new InvalidOperationException("Reopening lyrics must retain the same paused track and clock.");
        File.WriteAllText(Path.Combine(folder, "native-player.json"), JsonSerializer.Serialize(new
        {
            passed = true, assemblySha256 = App.AssemblySha256, assertions = 7,
            scope = "Native player launch, compact layout, cover selection, shared clock, opening, hiding and reopening the lyric window"
        }, LyricsFile.JsonOptions));
        bool client = Environment.GetCommandLineArgs().Contains("--netease-client-test");
        if (client || Environment.GetCommandLineArgs().Contains("--netease-test"))
        {
            await VerifyNetEaseAsync(client);
            Close();
            return;
        }
        await _lyrics.RunSelfTestAsync();

    }

    private void CapturePlayer(string folder, string filename)
    {
        var image = new RenderTargetBitmap((int)PlayerContent.ActualWidth, (int)PlayerContent.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(PlayerContent);
        File.WriteAllBytes(Path.Combine(folder, filename), Artwork.Encode(image));
    }

    private async Task VerifyNetEaseAsync(bool client)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var tracks = client
            ? await NetEaseMusic.SearchClientAsync("高尚 薛之谦", timeout.Token)
            : await NetEaseMusic.SearchAsync("晴天", timeout.Token);
        long expectedId = client ? 466122271 : 3356343819;
        var track = tracks.FirstOrDefault(item => item.Playable && item.OriginalId == expectedId)
            ?? throw new InvalidOperationException("The NetEase playback fixture is unavailable under the current authorization.");
        try
        {
            await _lyrics.PlayNetEaseAsync(track, timeout.Token);
            await WaitForAsync(() => _lyrics.Clock.IsPlaying && _lyrics.Clock.PositionMs > 600);
            using var decoder = Process.GetProcessById(await NetEaseMusic.GetPlaybackProcessIdAsync(timeout.Token));
            if (decoder.MainWindowHandle != IntPtr.Zero)
                throw new InvalidOperationException("The NetEase audio decoder opened an independent window.");
            await WaitForAsync(() => _lyrics.TrackArtwork is BitmapImage);
            RefreshPlayer();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            CapturePlayer(Path.Combine(App.DataFolder, "verification"), "player-netease.png");
            var cover = (BitmapImage)_lyrics.TrackArtwork!;
            string[] coverSurfaces = ["WebImageAlbum", "WebImageAlbumSmall", "WebImageBackground"];
            var lyricImages = WordEdgeChecks.Find(_lyrics).OfType<Reference.ArtworkImage>().ToArray();
            if (cover.PixelWidth == 0 || !ReferenceEquals(Cover.ImageSource, cover) ||
                coverSurfaces.Any(name => !lyricImages.Any(image => image.Name == name && ReferenceEquals(image.Source, cover))))
                throw new InvalidOperationException("The NetEase cover did not reach the player, lyric covers and background.");
            if (_lyrics.CurrentSong.Title != track.Title || _lyrics.CurrentSong.Lines.Count == 0)
                throw new InvalidOperationException("The authorized track and its timed lyrics did not reach the shared player.");
            int wordTimedLines = _lyrics.CurrentSong.Lines.Count(line => line.Words.Count > 0);
            double decodedDuration = _lyrics.Clock.DurationMs;
            if (client && (wordTimedLines == 0 || Math.Abs(decodedDuration - track.DurationMs) > 2500))
                throw new InvalidOperationException("The client track did not provide full-length audio and word-timed lyrics.");
            _lyrics.Seek(28250);
            await WaitForAsync(() => _lyrics.Clock.PositionMs is >= 28250 and < 31000);
            var audio = await NetEaseMusic.ReadPlaybackAsync(timeout.Token);
            if (audio.PositionMs is < 28250 or >= 31000)
                throw new InvalidOperationException("The audio decoder did not seek with the shared clock.");
            _lyrics.TogglePlay();
            await WaitForAsync(() => !_lyrics.Clock.IsPlaying);
            if ((await NetEaseMusic.ReadPlaybackAsync(timeout.Token)).Playing)
                throw new InvalidOperationException("The audio decoder did not pause with the shared clock.");
            _lyrics.TogglePlay();
            await WaitForAsync(() => _lyrics.Clock.IsPlaying);
            _lyrics.Close();
            if (_lyrics.IsVisible || !_lyrics.Clock.IsPlaying)
                throw new InvalidOperationException("Closing the lyric window interrupted NetEase playback.");
            await _lyrics.LoadSongAsync(0);
            if (_lyrics.HasNetEasePlayback || _lyrics.PlaybackSource == "网易云音乐")
                throw new InvalidOperationException("Switching sources left NetEase playback active.");
            string report = client ? "native-netease-client.json" : "native-netease.json";
            File.WriteAllText(Path.Combine(App.DataFolder, "verification", report), JsonSerializer.Serialize(new
            {
                passed = true, assemblySha256 = App.AssemblySha256, trackId = track.OriginalId, decodedDurationMs = decodedDuration, wordTimedLines,
                coverWidth = cover.PixelWidth, coverHeight = cover.PixelHeight,
                decoderWindow = false,
                scope = "Authorized search, timed lyrics, album artwork, windowless audio, seek, pause, resume, hidden lyrics, local source switch"
            }, LyricsFile.JsonOptions));
        }
        finally { await _lyrics.StopNetEaseAsync(); }

        async Task WaitForAsync(Func<bool> condition)
        {
            while (!condition())
                await Task.Delay(50, timeout.Token);
        }
    }
}
