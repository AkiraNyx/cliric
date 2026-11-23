using System.IO;
using System.Windows;
using System.Windows.Media;
using Cliric.Core;

namespace Cliric.App;

public sealed partial class MainWindow
{
    private readonly bool _hostedByPlayer;
    private Task? _playbackStartup;
    private bool _lyricsLoaded;
    private readonly Dictionary<string, NetEaseTrack> _netEaseTracks = [];
    private readonly HashSet<string> _unloadedQueueLyrics = [];
    private readonly List<Song> _playNext = [];
    private Song? _pendingQueueEntry;
    private long _queueSelection;
    internal long QueueSelection => _queueSelection;
    private readonly SemaphoreSlim _netEaseCommands = new(1);
    private bool _netEaseActive, _readingNetEase, _netEasePlayRequested;
    private bool _audioOpening;
    private int _playbackLoads;
    private double _nextNetEaseSample;

    internal IReadOnlyList<Song> Playlist => _songs;
    internal int TrackIndex => _songIndex;
    internal ImageSource? TrackArtwork => _artwork.Source;
    internal string? QueueCoverUrl(Song song) => _netEaseTracks.TryGetValue(song.Id, out var track) ? track.CoverUrl : null;
    internal string TrackArtist => _artist.Text;
    internal string PlaybackSource => _system is not null
        ? _system.Connected ? "系统媒体" : "等待系统媒体"
        : _netEaseTracks.ContainsKey(_song.Id) ? "网易云音乐" : _audioLoaded ? "本地音频" : "歌词时间轴";
    internal string PlaybackNotice => _wall.Elapsed.TotalSeconds < _noticeUntil ? _status.Text : "";
    internal bool Playing => _navigationRequest != 0 ? _navigationPlaying : _netEaseActive ? _netEasePlayRequested : _clock.IsPlaying;
    internal bool PlaybackLoading => _playbackLoads > 0 || _audioOpening || _navigationRequest != 0;
    internal bool CanControlPlayback => _system?.Connected ?? (_songIndex >= 0);
    internal bool CanOpenLyrics => CanControlPlayback && !PlaybackLoading;
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
        PublishMediaPosition(position);
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
            if (!IsVisible || WindowState == WindowState.Minimized)
                UpdateBackground(0);
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

    internal Task<bool> PlayNetEaseAsync(NetEaseTrack track, CancellationToken cancellationToken) =>
        WhilePlaybackLoadsAsync(() => PlayNetEaseCoreAsync(track, cancellationToken));

    private async Task<bool> PlayNetEaseCoreAsync(NetEaseTrack track, CancellationToken cancellationToken)
    {
        if (!track.Playable)
            throw new InvalidOperationException("当前授权不可播放这首歌曲。");
        long selection = ++_queueSelection;
        var song = await NetEaseMusic.LoadLyricsAsync(track, cancellationToken, _options.UseLyricifyParser);
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed || selection != _queueSelection)
            return false;
        int index = _songs.FindIndex(existing => existing.Id == song.Id);
        if (index < 0)
        {
            index = _songs.Count;
            _songs.Add(song);
        }
        else
        {
            UpdateQueuedLyrics(song.Id, song);
        }
        _netEaseTracks[song.Id] = track;
        await NavigateLocalAsync(index, startPlaying: true, cancellationToken);
        return !_disposed && _song.Id == song.Id && _netEasePlayRequested;
    }

    internal void QueueNetEase(NetEaseTrack track, bool next)
    {
        if (_navigationRequest == 0)
            ++_queueSelection;
        var song = CreateQueueSong(track);
        int index = next ? _songIndex + 1 : _songs.Count;
        _songs.Insert(index, song);
        if (next)
            _playNext.Insert(0, song);
        UpdatePlaybackAvailability();
        if (_lyricsShown)
            RefreshQueue();
        Notify(next ? $"《{song.Title}》将插播" : $"已将《{song.Title}》添加至队列末尾");
    }

    internal Task<bool> PlayNetEasePlaylistAsync(IReadOnlyList<NetEaseTrack> tracks, long expectedSelection, CancellationToken token) =>
        WhilePlaybackLoadsAsync(async () =>
        {
            if (expectedSelection != _queueSelection || _disposed)
                return false;
            var playable = tracks.Where(track => track.Playable).ToArray();
            if (playable.Length == 0)
                throw new InvalidOperationException("歌单没有可播放的歌曲。");
            long selection = ++_queueSelection;
            Exception? lastError = null;
            for (int index = 0; index < playable.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                if (_disposed || selection != _queueSelection)
                    return false;
                Song first;
                try { first = await NetEaseMusic.LoadLyricsAsync(playable[index], token, _options.UseLyricifyParser); }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    lastError = error;
                    continue;
                }
                token.ThrowIfCancellationRequested();
                if (_disposed || selection != _queueSelection)
                    return false;

                ReplaceNetEaseQueue(playable, index, first);
                await NavigateLocalAsync(index, startPlaying: true);
                return !_disposed && ReferenceEquals(_song, first) && _netEasePlayRequested;
            }
            throw new InvalidOperationException("无法载入歌单中的歌曲。", lastError);
        });

    internal void ReplaceNetEaseQueue(NetEaseTrack[] playable, int loadedIndex, Song loaded)
    {
        _songs.Clear();
        _netEaseTracks.Clear();
        _unloadedQueueLyrics.Clear();
        _playNext.Clear();
        _netEaseTracks[loaded.Id] = playable[loadedIndex];
        for (int index = 0; index < playable.Length; index++)
        {
            var track = playable[index];
            _songs.Add(index == loadedIndex ? loaded :
                CreateQueueSong(track, $"netease-{track.OriginalId}" == loaded.Id ? loaded : null));
        }
    }

    private Song CreateQueueSong(NetEaseTrack track) =>
        CreateQueueSong(track, _songs.FirstOrDefault(song => song.Id == $"netease-{track.OriginalId}"));

    private Song CreateQueueSong(NetEaseTrack track, Song? existing)
    {
        if (!track.Playable)
            throw new InvalidOperationException("当前授权不可播放这首歌曲。");
        string id = $"netease-{track.OriginalId}";
        var song = new Song
        {
            Id = id, Title = track.Title, Artist = track.Artist, Album = track.Album, DurationMs = track.DurationMs,
            Untimed = existing?.Untimed ?? true, Lines = existing?.Lines ?? [], CoverPath = existing?.CoverPath,
            AlbumId = existing?.AlbumId, Credit = existing?.Credit ?? "", ArtworkVideoPath = existing?.ArtworkVideoPath
        };
        _netEaseTracks.TryAdd(id, track);
        if (existing is null)
            _unloadedQueueLyrics.Add(id);
        return song;
    }

    private async Task PrepareQueuedLyricsAsync(Song song, CancellationToken token)
    {
        if (!_unloadedQueueLyrics.Contains(song.Id) || !_netEaseTracks.TryGetValue(song.Id, out var track))
            return;
        var lyrics = await NetEaseMusic.LoadLyricsAsync(track, token, _options.UseLyricifyParser);
        token.ThrowIfCancellationRequested();
        if (_netEaseTracks.TryGetValue(song.Id, out var currentTrack) && ReferenceEquals(track, currentTrack))
            UpdateQueuedLyrics(song.Id, lyrics);
    }

    private void UpdateQueuedLyrics(string id, Song lyrics)
    {
        _unloadedQueueLyrics.Remove(id);
        foreach (var song in _songs.Where(song => song.Id == id))
        {
            song.Lines = lyrics.Lines;
            song.Untimed = lyrics.Untimed;
            song.DurationMs = lyrics.DurationMs;
            song.AlbumId = lyrics.AlbumId;
            song.Credit = lyrics.Credit;
        }
    }

    internal async Task RemoveQueueEntryAsync(Song entry)
    {
        int index = _songs.IndexOf(entry);
        if (index < 0)
            return;
        _songs.RemoveAt(index);
        if (_navigationRequest == 0)
            ++_queueSelection;
        _playNext.Remove(entry);
        if (!_songs.Any(song => song.Id == entry.Id))
        {
            _netEaseTracks.Remove(entry.Id);
            _unloadedQueueLyrics.Remove(entry.Id);
        }
        if (index != _songIndex)
        {
            if (index < _songIndex)
                _songIndex--;
        }
        else if (_system is not null)
        {
            _songIndex = -1;
        }
        else
        {
            bool playing = Playing;
            _songIndex = -1;
            var successor = _songs.Count > 0 ? _songs[Math.Min(index, _songs.Count - 1)] : null;
            long selection = _queueSelection + 1;
            await WhilePlaybackLoadsAsync(ClearQueuePlaybackAsync);
            if (!_disposed && _queueSelection == selection && _songs.Count > 0)
            {
                int next = successor is null ? -1 : _songs.IndexOf(successor);
                await NavigateLocalAsync(next >= 0 ? next : Math.Min(index, _songs.Count - 1), playing);
            }
        }
        UpdatePlayButtons();
    }

    private async Task ClearQueuePlaybackAsync()
    {
        ++_queueSelection;
        int generation = ++_loadGeneration;
        _navigationRequest = 0;
        _navigationPlaying = false;
        _pendingQueueEntry = null;
        _playerSeek = null;
        ++_playerSeekRevision;
        _clock.Load(0);
        _suppressMediaEnded = true;
        _audio.Stop();
        _audio.Close();
        _audioOpening = _audioLoaded = _suppressMediaEnded = false;
        _song = new Song { Title = "未播放", Artist = "", Untimed = true };
        _title.Text = _smallTitle.Text = _song.Title;
        _artist.Text = _smallArtist.Text = "";
        _artwork.Source = _smallArtwork.Source = null;
        _hasExplicitArtwork = false;
        IsHearted = false;
        try
        {
            if (_netEaseActive)
                await StopNetEaseAsync();
        }
        finally
        {
            if (!_disposed && generation == _loadGeneration)
            {
                _rebuildPending = true;
                UpdatePlayButtons();
                if (_lyricsShown)
                    ShowLibrary();
            }
        }
    }

    private readonly CancellationTokenSource _queueLifetime = new();

    internal int NextQueueIndex(bool atEnd = false)
    {
        if (_songs.Count == 0)
            return -1;
        if (atEnd && _repeat && _songIndex >= 0)
            return _songIndex;
        int current = _pendingQueueEntry is { } pending ? _songs.IndexOf(pending) : -1;
        if (current < 0)
            current = _songIndex;
        if (_playNext.Count > 0)
        {
            int queued = _songs.IndexOf(_playNext[0]);
            if (queued >= 0)
                return queued;
        }
        if (!_shuffle || _songs.Count < 2 || current < 0)
            return (current + 1) % _songs.Count;
        int next = Random.Shared.Next(_songs.Count - 1);
        return next >= current ? next + 1 : next;
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

    internal async Task WhilePlaybackLoadsAsync(Func<Task> load)
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

    private async Task<T> WhilePlaybackLoadsAsync<T>(Func<Task<T>> load)
    {
        T result = default!;
        await WhilePlaybackLoadsAsync(async () => { result = await load(); });
        return result;
    }

    private void UpdatePlaybackAvailability()
    {
        foreach (var button in _reference.Elements.OfType<Reference.ImageButton>())
            if (Reference.InputMap.GetClick(button) is "ImageClickablePauseButton_Click" or "ImageClickablePlayButton_Click")
                button.IsEnabled = CanControlPlayback && !PlaybackLoading;
        _library?.UpdatePlaybackAvailability();
    }

    internal bool HasNetEasePlayback => _netEaseActive;
    private bool PlayerScrubbing => _library?.IsSeeking == true;

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

    internal void SeekFromPlayer(double position, bool preview)
    {
        if (_system is not null || !_netEaseTracks.ContainsKey(_song.Id))
        {
            Seek(position);
            return;
        }
        long revision = ++_playerSeekRevision;
        if (!_netEaseActive || PlaybackLoading ||
            PlayerSeekTarget(position, _clock.DurationMs, preview) is null)
        {
            if (preview)
                _playerSeek = null;
            return;
        }
        _playerSeek = (revision, _loadGeneration, position, preview);
        if (!_sendingPlayerSeek)
            _ = SendPlayerSeeksAsync();
    }

    private long _playerSeekRevision;
    private (long Revision, int Generation, double Position, bool Preview)? _playerSeek;
    private bool _sendingPlayerSeek;

    internal static double? PlayerSeekTarget(double position, double duration, bool preview)
    {
        if (!double.IsFinite(position) || !double.IsFinite(duration) || duration <= 0)
            return null;
        double guard = Math.Min(preview ? 750 : 120, duration / 4);
        if (preview && position >= duration - guard)
            return null;
        return Math.Clamp(position, 0, duration - guard);
    }

    private async Task SendPlayerSeeksAsync()
    {
        _sendingPlayerSeek = true;
        try
        {
            while (_playerSeek is { } request)
            {
                _playerSeek = null;
                if (request.Generation == _loadGeneration && !_disposed)
                    await SendPlayerSeekAsync(request.Revision, request.Generation, request.Position, request.Preview);
            }
        }
        finally { _sendingPlayerSeek = false; }
    }

    private async Task SendPlayerSeekAsync(long revision, int generation, double position, bool preview)
    {
        await _netEaseCommands.WaitAsync();
        try
        {
            if (_disposed || !_netEaseActive || generation != _loadGeneration || revision != _playerSeekRevision)
                return;
            var playback = await NetEaseMusic.ReadPlaybackAsync(CancellationToken.None);
            if (playback.Ended || _disposed || generation != _loadGeneration || revision != _playerSeekRevision)
                return;
            if (PlayerSeekTarget(position, playback.DurationMs > 0 ? playback.DurationMs : _clock.DurationMs, preview) is not { } target)
                return;
            try { await NetEaseMusic.SetAsync("time-pos", target / 1000, CancellationToken.None); }
            catch (InvalidOperationException)
            {
                if ((await NetEaseMusic.ReadPlaybackAsync(CancellationToken.None)).Ended)
                    return;
                throw;
            }
            if (!_disposed && generation == _loadGeneration && revision == _playerSeekRevision)
            {
                _clock.Seek(target);
                _nextNetEaseSample = 0;
            }
        }
        catch (Exception error)
        {
            if (!_disposed && generation == _loadGeneration && revision == _playerSeekRevision)
                Notify(error.Message);
        }
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
                // Keep the seek bar's preview and current song until the pointer is released.
                _clock.Pause();
                if (!PlaybackLoading && !PlayerScrubbing)
                {
                    _netEasePlayRequested = false;
                    advance = true;
                    UpdatePlayButtons();
                }
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
                _ = AdvanceNetEaseQueueAsync();
        }
    }

    private async Task AdvanceNetEaseQueueAsync()
    {
        try { await NavigateLocalAsync(NextQueueIndex(atEnd: true), startPlaying: true); }
        catch (Exception error) { if (!_disposed) Notify(error.Message); }
    }
}
