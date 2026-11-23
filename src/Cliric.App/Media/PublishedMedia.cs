using System.Windows.Media.Imaging;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace Cliric.App;

internal sealed class PublishedMedia : IDisposable
{
    private readonly MediaPlayer _player = new();
    private readonly SystemMediaTransportControls _controls;
    private (string Title, string Artist, string Album) _metadata;
    private BitmapSource? _cover;
    private InMemoryRandomAccessStream? _thumbnail;
    private long _nextUpdate, _generation = -1;
    private bool _playing, _disposed;

    internal PublishedMedia(Action<SystemMediaTransportControlsButton> command, Action<double> seek)
    {
        _player.CommandManager.IsEnabled = false;
        _controls = _player.SystemMediaTransportControls;
        _controls.ButtonPressed += (_, args) => command(args.Button);
        _controls.PlaybackPositionChangeRequested += (_, args) => seek(args.RequestedPlaybackPosition.TotalMilliseconds);
    }

    internal void Update(string title, string artist, string album, BitmapSource? cover,
        double position, double duration, bool playing, long generation)
    {
        var data = (title, artist, album);
        bool changed = !_controls.IsEnabled || data != _metadata;
        if (changed)
        {
            _metadata = data;
            var display = _controls.DisplayUpdater;
            display.Type = MediaPlaybackType.Music;
            display.MusicProperties.Title = title;
            display.MusicProperties.Artist = artist;
            display.MusicProperties.AlbumTitle = album;
            display.Update();
        }
        if (!ReferenceEquals(_cover, cover) || changed)
        {
            _cover = cover;
            _ = UpdateThumbnailAsync(cover, data);
        }

        long now = Environment.TickCount64;
        if (!changed && playing == _playing && generation == _generation && now < _nextUpdate)
            return;
        _playing = playing;
        _generation = generation;
        _nextUpdate = now + 400;
        _controls.IsPlayEnabled = _controls.IsPauseEnabled = true;
        _controls.IsNextEnabled = _controls.IsPreviousEnabled = true;
        _controls.IsEnabled = true;
        _controls.PlaybackStatus = playing ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;
        var end = TimeSpan.FromMilliseconds(duration);
        _controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
        {
            EndTime = end, MaxSeekTime = end,
            Position = TimeSpan.FromMilliseconds(Math.Clamp(position, 0, duration))
        });
    }

    internal void Hide()
    {
        if (!_controls.IsEnabled)
            return;
        _controls.PlaybackStatus = MediaPlaybackStatus.Closed;
        _controls.IsEnabled = false;
    }

    private async Task UpdateThumbnailAsync(BitmapSource? cover, (string, string, string) data)
    {
        InMemoryRandomAccessStream? image = null;
        try
        {
            _controls.DisplayUpdater.Thumbnail = null;
            _controls.DisplayUpdater.Update();
            if (cover is null)
                return;
            image = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(image.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(Artwork.Encode(cover));
                await writer.StoreAsync();
            }
            if (_disposed || !ReferenceEquals(_cover, cover) || _metadata != data)
                return;
            _controls.DisplayUpdater.Thumbnail = RandomAccessStreamReference.CreateFromStream(image);
            _controls.DisplayUpdater.Update();
            _thumbnail?.Dispose();
            _thumbnail = image;
            image = null;
        }
        catch (Exception error) { App.Log($"SMTC artwork: {error.Message}"); }
        finally { image?.Dispose(); }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _thumbnail?.Dispose();
        _player.Dispose();
    }
}
