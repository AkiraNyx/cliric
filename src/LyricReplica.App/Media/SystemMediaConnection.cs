using System.IO;
using System.Windows.Threading;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace LyricReplica.App;

public sealed record MediaSnapshot(string Title, string Artist, string Album, double PositionMs, double DurationMs, bool Playing, byte[]? Artwork = null, bool? ShuffleActive = null, MediaPlaybackAutoRepeatMode? RepeatMode = null, bool CanShuffle = false, bool CanRepeat = false);

public sealed class SystemMediaConnection(Dispatcher dispatcher) : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(400) };
    private bool _disposed, _refreshing;
    private int _propertiesRevision;
    private (string Title, string Artist, string Album)? _artworkIdentity;
    private byte[]? _artwork;
    public event Action<MediaSnapshot>? Changed;
    public event Action<string>? Error;
    public event Action? Disconnected;
    public bool Connected => _session is not null;

    public async Task ConnectAsync()
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        if (_disposed)
            return;
        _manager = manager;
        _manager.CurrentSessionChanged += OnSessionChanged;
        SetSession(_manager.GetCurrentSession());
        _timer.Tick += OnTick;
        _timer.Start();
        await RefreshAsync();
    }
    private void OnTick(object? sender, EventArgs e) => _ = RefreshAsync();
    private void OnSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        => dispatcher.InvokeAsync(() => { if (!_disposed) { SetSession(sender.GetCurrentSession()); _ = RefreshAsync(); } });
    private void SetSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_session is not null)
        {
            _session.PlaybackInfoChanged -= OnPlayback;
            _session.TimelinePropertiesChanged -= OnTimeline;
            _session.MediaPropertiesChanged -= OnProperties;
        }
        _session = session;
        _propertiesRevision++;
        _artworkIdentity = null;
        _artwork = null;
        if (_session is not null)
        {
            _session.PlaybackInfoChanged += OnPlayback;
            _session.TimelinePropertiesChanged += OnTimeline;
            _session.MediaPropertiesChanged += OnProperties;
        }
        else if (!_disposed)
            Disconnected?.Invoke();
    }
    private void OnPlayback(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => dispatcher.InvokeAsync(() => _ = RefreshAsync());
    private void OnTimeline(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) => dispatcher.InvokeAsync(() => _ = RefreshAsync());
    private void OnProperties(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => dispatcher.InvokeAsync(() => { _propertiesRevision++; _artworkIdentity = null; _ = RefreshAsync(); });
    private async Task RefreshAsync()
    {
        if (_disposed || _refreshing || _session is null)
            return;
        _refreshing = true;
        try
        {
            var session = _session;
            int revision = _propertiesRevision;
            var properties = await session.TryGetMediaPropertiesAsync();
            if (_disposed || session != _session || revision != _propertiesRevision)
                return;
            var identity = (properties.Title, properties.Artist, properties.AlbumTitle);
            if (_artworkIdentity != identity)
            {
                byte[]? artwork = null;
                try
                {
                    artwork = await ReadArtworkAsync(properties.Thumbnail);
                }
                catch (Exception e) { if (!_disposed && session == _session && revision == _propertiesRevision) Error?.Invoke("媒体封面：" + e.Message); }
                if (_disposed || session != _session || revision != _propertiesRevision)
                    return;
                _artwork = artwork;
                _artworkIdentity = identity;
            }
            var timeline = session.GetTimelineProperties();
            var playback = session.GetPlaybackInfo();
            bool playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            double position = timeline.Position.TotalMilliseconds + (playing ? Math.Max(0, (DateTimeOffset.Now - timeline.LastUpdatedTime).TotalMilliseconds) : 0);
            Changed?.Invoke(new MediaSnapshot(properties.Title, properties.Artist, properties.AlbumTitle, position, (timeline.EndTime - timeline.StartTime).TotalMilliseconds, playing, _artwork, playback.IsShuffleActive, playback.AutoRepeatMode, playback.Controls.IsShuffleEnabled, playback.Controls.IsRepeatEnabled));
        }
        catch (Exception e) { if (!_disposed) Error?.Invoke(e.Message); }
        finally { _refreshing = false; }
    }
    internal static async Task<byte[]?> ReadArtworkAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null)
            return null;
        using var stream = await thumbnail.OpenReadAsync();
        if (stream.Size == 0)
            return null;
        if (stream.Size > Artwork.MaximumMediaBytes)
            throw new InvalidDataException("封面文件超过16 MiB。");
        uint size = (uint)stream.Size;
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        if (await reader.LoadAsync(size) != size)
            throw new InvalidDataException("媒体封面未完整读取。");
        var bytes = new byte[size];
        reader.ReadBytes(bytes);
        return bytes;
    }
    private GlobalSystemMediaTransportControlsSession RequireSession() => _session ?? throw new InvalidOperationException("系统媒体会话暂不可用，请等待播放器恢复。");
    public async Task PlayAsync()
    {
        if (!await RequireSession().TryPlayAsync())
            throw new InvalidOperationException("播放器未接受播放命令。");
    }
    public async Task PauseAsync()
    {
        if (!await RequireSession().TryPauseAsync())
            throw new InvalidOperationException("播放器未接受暂停命令。");
    }
    public async Task SeekAsync(double ms)
    {
        if (!await RequireSession().TryChangePlaybackPositionAsync(TimeSpan.FromMilliseconds(ms).Ticks))
            throw new InvalidOperationException("播放器未接受跳播命令。");
    }
    public async Task NextAsync()
    {
        if (!await RequireSession().TrySkipNextAsync())
            throw new InvalidOperationException("播放器未接受下一曲命令。");
    }
    public async Task PreviousAsync()
    {
        if (!await RequireSession().TrySkipPreviousAsync())
            throw new InvalidOperationException("播放器未接受上一曲命令。");
    }
    public async Task ShuffleAsync(bool enabled)
    {
        var session = RequireSession();
        if (!session.GetPlaybackInfo().Controls.IsShuffleEnabled)
            throw new InvalidOperationException("此媒体会话未提供随机播放控制。");
        if (!await session.TryChangeShuffleActiveAsync(enabled))
            throw new InvalidOperationException("播放器未接受随机播放命令。");
    }
    public async Task RepeatAsync(MediaPlaybackAutoRepeatMode mode)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        var session = RequireSession();
        if (!session.GetPlaybackInfo().Controls.IsRepeatEnabled)
            throw new InvalidOperationException("此媒体会话未提供循环控制。");
        if (!await session.TryChangeAutoRepeatModeAsync(mode))
            throw new InvalidOperationException("播放器未接受循环命令。");
    }
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        if (_manager is not null)
            _manager.CurrentSessionChanged -= OnSessionChanged;
        SetSession(null);
        _manager = null;
    }
}
