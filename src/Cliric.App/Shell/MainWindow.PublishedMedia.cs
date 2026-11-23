using System.Windows.Media.Imaging;
using Windows.Media;

namespace Cliric.App;

public sealed partial class MainWindow
{
    private PublishedMedia? _publishedMedia;
    private bool _mediaPublicationFailed;

    private void PublishMediaPosition(double position)
    {
        if (_mediaPublicationFailed || !IsLoaded || _args.Contains("--verify-offscreen") ||
            _args.Contains("--self-test") && !_args.Contains("--smtc-test"))
            return;
        try
        {
            if (_system is not null || _songIndex < 0 || _clock.DurationMs <= 0 || PlaybackLoading)
            {
                _publishedMedia?.Hide();
                return;
            }
            _publishedMedia ??= new PublishedMedia(
                button => Dispatcher.InvokeAsync(() => HandleMediaButton(button)),
                ms => Dispatcher.InvokeAsync(() => { if (!_disposed && _system is null) SeekFromPlayer(ms, preview: false); }));
            _publishedMedia.Update(_song.Title, TrackArtist, _song.Album, TrackArtwork as BitmapSource,
                position, _clock.DurationMs, _clock.IsPlaying, _clock.Generation);
        }
        catch (Exception error)
        {
            _mediaPublicationFailed = true;
            _publishedMedia?.Dispose();
            App.Log($"SMTC: {error.Message}");
        }
    }

    private void HandleMediaButton(SystemMediaTransportControlsButton button)
    {
        if (_disposed || _system is not null || PlaybackLoading)
            return;
        switch (button)
        {
            case SystemMediaTransportControlsButton.Play when !Playing:
            case SystemMediaTransportControlsButton.Pause when Playing:
            case SystemMediaTransportControlsButton.Stop when Playing:
                TogglePlay();
                break;
            case SystemMediaTransportControlsButton.Next:
                Next();
                break;
            case SystemMediaTransportControlsButton.Previous:
                Previous();
                break;
        }
    }
}
