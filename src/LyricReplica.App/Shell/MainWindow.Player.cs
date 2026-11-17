using System.IO;
using System.Windows;
using System.Windows.Media;
using LyricReplica.Core;

namespace LyricReplica.App;

public sealed partial class MainWindow
{
    private readonly bool _hostedByPlayer;
    private Task? _playbackStartup;
    private bool _lyricsLoaded;
    private readonly Dictionary<string, NetEaseTrack> _netEaseTracks = [];
    private readonly SemaphoreSlim _netEaseCommands = new(1);
    private bool _netEaseActive, _readingNetEase, _netEasePlayRequested;
    private bool _audioOpening;
    private int _playbackLoads;
    private double _nextNetEaseSample;

    internal IReadOnlyList<Song> Playlist => _songs;
    internal int TrackIndex => _songIndex;
    internal ImageSource? TrackArtwork => _artwork.Source;
    internal string TrackArtist => _artist.Text;
    internal string PlaybackSource => _system is not null
        ? _system.Connected ? "系统媒体" : "等待系统媒体"
        : _netEaseTracks.ContainsKey(_song.Id) ? "网易云音乐" : _audioLoaded ? "本地音频" : "歌词时间轴";
    internal string PlaybackNotice => _wall.Elapsed.TotalSeconds < _noticeUntil ? _status.Text : "";
    internal bool Playing => _navigationRequest != 0 ? _navigationPlaying : _netEaseActive ? _netEasePlayRequested : _clock.IsPlaying;
    internal bool PlaybackLoading => _playbackLoads > 0 || _audioOpening || _navigationRequest != 0;
    internal bool CanControlPlayback => _system?.Connected ?? (_songIndex >= 0);
    internal NetEaseTrack? ClientTrack => _system is null && _netEaseTracks.TryGetValue(_song.Id, out var track) && track.ClientSession ? track : null;
    internal string ActualNetEaseQuality { get; private set; } = "";
    internal bool ShuffleActive => _shuffle;
    internal bool RepeatActive => _repeat;
    internal bool CanShuffle => _system is null ? _songIndex >= 0 : _system.Connected && _remoteShuffle;
    internal bool CanRepeat => _system is null ? _songIndex >= 0 : _system.Connected && _remoteRepeat;
    private bool LyricsVisible => IsVisible && WindowState != WindowState.Minimized && (!_hostedByPlayer || _lyricsShown);

    internal Task StartPlaybackAsync() => _playbackStartup ??= InitializePlaybackAsync();

    private async Task InitializePlaybackAsync()
    {
        _audio.Volume = _options.Volume;
        if (_songs.Count > 0)
        {
            await LoadSongAsync(0);
            _clock.Seek(8500);
        }
        else
        {
            _title.Text = _smallTitle.Text = _song.Title;
            _artist.Text = _smallArtist.Text = "";
            UpdatePlayButtons();
        }
        if (_disposed)
            return;
        CompositionTarget.Rendering += OnRendering;
        if (_args.Length > 0 && !_args[0].StartsWith("--"))
            ImportLyrics(_args[0]);
    }

    internal void UpdatePlaybackWhileLyricsHidden()
    {
        if (!_disposed && !LyricsVisible)
            UpdatePlaybackPosition();
    }

    private double UpdatePlaybackPosition()
    {
        double position = _clock.PositionMs;
        if (_netEaseTracks.ContainsKey(_song.Id) && _system is null)
        {
            if (_netEaseActive && !_readingNetEase && _wall.Elapsed.TotalSeconds >= _nextNetEaseSample)
                _ = RefreshNetEaseClockAsync();
            return position;
        }
        if (_clock.IsPlaying && position >= _clock.DurationMs - .1 && _clock.DurationMs > 0)
        {
            EndTrack();
            position = _clock.PositionMs;
        }
        if (_audioLoaded && _clock.IsPlaying && Math.Abs(_audio.Position.TotalMilliseconds - position) > 150)
            _clock.Seek(_audio.Position.TotalMilliseconds);
        return position;
    }

    private void UpdateLyricsVisibility()
    {
        if (_disposed)
            return;
        if (_hostedByPlayer && !LyricsVisible)
        {
            _background.Update(_clock.IsPlaying, _portrait, false, _options, 0);
            StopArtworkVideo();
        }
        UpdateSleepRequest();
    }

    internal async Task AddAudioTrackAsync(string path)
    {
        try
        {
            _songs.Add(new Song
            {
                Title = Path.GetFileNameWithoutExtension(path),
                AudioPath = path,
                Untimed = true
            });
            await LoadSongAsync(_songs.Count - 1);
        }
        catch (Exception e) { Notify(e.Message); App.Log(e.ToString()); }
    }

    internal void ApplyPlayerSettings(double volume, bool realtimeSeek)
    {
        Guard(() =>
        {
            _options.Volume = volume;
            _options.RealtimeSeek = realtimeSeek;
            _options.Normalize();
            _audio.Volume = _options.Volume;
            App.SaveOptions(_options);
        });
        if (_netEaseActive)
            _ = ControlNetEaseAsync("volume", _options.Volume * 100);
    }

    internal Task PlayNetEaseAsync(NetEaseTrack track, CancellationToken cancellationToken) =>
        WhilePlaybackLoadsAsync(() => PlayNetEaseCoreAsync(track, cancellationToken));

    private async Task PlayNetEaseCoreAsync(NetEaseTrack track, CancellationToken cancellationToken)
    {
        if (!track.Playable)
            throw new InvalidOperationException("当前授权不可播放这首歌曲。");
        var song = await NetEaseMusic.LoadLyricsAsync(track, cancellationToken, _options.UseLyricifyParser);
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed)
            return;
        int index = _songs.FindIndex(existing => existing.Id == song.Id);
        if (index < 0)
        {
            index = _songs.Count;
            _songs.Add(song);
        }
        else
        {
            song.CoverPath = _songs[index].CoverPath;
            _songs[index] = song;
        }
        _netEaseTracks[song.Id] = track;
        await LoadSongAsync(index);
        if (!_disposed && _song.Id == song.Id && _system is null)
            await StartNetEaseAsync(track, cancellationToken);
    }

    private async Task UpdateNetEaseCoverAsync(string url, int generation)
    {
        try
        {
            var cover = await NetEaseMusic.LoadCoverAsync(url);
            if (cover is null || _disposed || generation != _loadGeneration || _system is not null || _hasExplicitArtwork)
                return;
            _artwork.Source = _smallArtwork.Source = cover;
            await _background.SetArtworkAsync(cover, _song.Id + ":netease:" + url);
        }
        catch (Exception exception) { App.Log("NetEase artwork: " + exception.GetType().Name); }
    }

    private Task StartNetEaseAsync(NetEaseTrack track, CancellationToken cancellationToken = default, double position = 0, bool playing = true) =>
        WhilePlaybackLoadsAsync(() => StartNetEaseCoreAsync(track, cancellationToken, position, playing));

    private async Task StartNetEaseCoreAsync(NetEaseTrack track, CancellationToken cancellationToken, double position, bool playing)
    {
        var song = _song;
        int generation = _loadGeneration;
        await _netEaseCommands.WaitAsync(cancellationToken);
        try
        {
            if (_disposed || generation != _loadGeneration || _system is not null || _netEaseActive)
                return;
            _netEaseActive = true;
            try
            {
                string quality = await NetEaseMusic.StartAsync(track, _options.Volume, _options.NetEaseQuality, cancellationToken, position, playing);
                if (generation == _loadGeneration && !_disposed && ReferenceEquals(song, _song))
                {
                    ActualNetEaseQuality = quality;
                    _netEasePlayRequested = playing;
                    if (position > 0)
                        _clock.Seek(position);
                    UpdatePlayButtons();
                }
            }
            catch
            {
                _netEasePlayRequested = false;
                try
                {
                    await NetEaseMusic.StopAsync(CancellationToken.None);
                    _netEaseActive = false;
                }
                catch (Exception exception) { if (!_disposed) Notify(exception.Message); }
                throw;
            }
        }
        finally { _netEaseCommands.Release(); }
    }

    internal async Task SwitchNetEaseQualityAsync(NetEaseTrack track, string quality, CancellationToken token)
    {
        if (PlaybackLoading || !ReferenceEquals(ClientTrack, track))
            return;
        string previous = _options.NetEaseQuality;
        if (previous == quality && (!_netEaseActive || ActualNetEaseQuality == quality))
            return;
        bool restart = _netEaseActive;
        double position = _clock.PositionMs;
        bool playing = Playing;
        int generation = _loadGeneration;
        await WhilePlaybackLoadsAsync(async () =>
        {
            if (restart)
                await StopNetEaseAsync();
            if (_disposed || generation != _loadGeneration)
                return;
            _options.NetEaseQuality = quality;
            try
            {
                if (restart)
                    await StartNetEaseAsync(track, token, position, playing);
            }
            catch
            {
                _options.NetEaseQuality = previous;
                if (restart && !_disposed && generation == _loadGeneration && !token.IsCancellationRequested)
                {
                    try { await StartNetEaseAsync(track, token, position, playing); }
                    catch (Exception exception) { App.Log("NetEase restore after quality switch: " + exception.GetType().Name); }
                }
                throw;
            }
            App.SaveOptions(_options);
        });
    }

    private async Task WhilePlaybackLoadsAsync(Func<Task> load)
    {
        _playbackLoads++;
        try
        {
            UpdatePlaybackAvailability();
            await load();
        }
        finally
        {
            _playbackLoads--;
            if (!_disposed)
                UpdatePlaybackAvailability();
        }
    }

    private void UpdatePlaybackAvailability()
    {
        foreach (var button in _reference.Elements.OfType<Reference.ImageButton>())
            if (Reference.InputMap.GetClick(button) is "ImageClickablePauseButton_Click" or "ImageClickablePlayButton_Click")
                button.IsEnabled = CanControlPlayback && !PlaybackLoading;
        _library?.UpdatePlaybackAvailability();
    }

    internal bool HasNetEasePlayback => _netEaseActive;

    internal async Task StopNetEaseAsync()
    {
        await _netEaseCommands.WaitAsync();
        try
        {
            if (!_netEaseActive)
                return;
            await NetEaseMusic.StopAsync(CancellationToken.None);
            _netEaseActive = _netEasePlayRequested = false;
            ActualNetEaseQuality = "";
            _clock.Pause();
            if (!_disposed)
                UpdatePlayButtons();
        }
        finally { _netEaseCommands.Release(); }
    }

    private async Task ControlNetEaseAsync(string property, object value)
    {
        int generation = _loadGeneration;
        await _netEaseCommands.WaitAsync();
        try
        {
            if (_disposed || !_netEaseActive || generation != _loadGeneration)
                return;
            if (property == "pause")
                value = _netEasePlayRequested;
            await NetEaseMusic.SetAsync(property, value, CancellationToken.None);
            if (generation != _loadGeneration || _disposed)
                return;
            if (property == "pause" && value is bool paused)
            {
                _netEasePlayRequested = !paused;
                if (paused)
                    _clock.Pause();
                UpdatePlayButtons();
            }
            if (property == "time-pos" && value is double position)
                _clock.Seek(position * 1000);
            _nextNetEaseSample = 0;
        }
        catch (Exception exception) { if (!_disposed) Notify(exception.Message); }
        finally { _netEaseCommands.Release(); }
    }

    private async Task RefreshNetEaseClockAsync()
    {
        if (!_netEaseCommands.Wait(0))
            return;
        _readingNetEase = true;
        int generation = _loadGeneration;
        bool advance = false;
        try
        {
            var state = await NetEaseMusic.ReadPlaybackAsync(CancellationToken.None);
            if (_disposed || generation != _loadGeneration || !_netEaseActive)
                return;
            if (state.Ended && _netEasePlayRequested)
            {
                _netEasePlayRequested = false;
                _clock.Pause();
                advance = true;
                UpdatePlayButtons();
                return;
            }
            if (state.DurationMs is > 0 and <= 86_400_000 && Math.Abs(state.DurationMs - _clock.DurationMs) > 10)
                _clock.Load(state.DurationMs);
            if (Math.Abs(state.PositionMs - _clock.PositionMs) > 140 || state.Playing != _clock.IsPlaying)
                _clock.Seek(state.PositionMs);
            if (state.Playing)
                _clock.Play();
            else
                _clock.Pause();
            UpdatePlayButtons();
        }
        catch (Exception exception)
        {
            if (!_disposed && generation == _loadGeneration)
            {
                _clock.Pause();
                _netEasePlayRequested = false;
                await Task.Delay(500);
                Notify(exception.Message);
                UpdatePlayButtons();
            }
        }
        finally
        {
            _readingNetEase = false;
            _nextNetEaseSample = _wall.Elapsed.TotalSeconds + .25;
            _netEaseCommands.Release();
            if (advance && !_disposed && generation == _loadGeneration)
                _ = NavigateLocalAsync(_repeat ? _songIndex : _shuffle ? Random.Shared.Next(_songs.Count) : _songIndex + 1, startPlaying: true);
        }
    }
}
