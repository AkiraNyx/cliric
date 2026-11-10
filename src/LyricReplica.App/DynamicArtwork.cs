using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace LyricReplica.App;

public sealed partial class MainWindow
{
    private MediaElement _artworkVideo=null!;
    private string? _videoPath,_failedVideo,_requestedVideo;
    private bool _videoPlaying;
    private void InitializeArtworkVideo()
    {
        _artworkVideo=_reference.Get<MediaElement>("WebImageHumanVideo");
        _artworkVideo.LoadedBehavior=MediaState.Manual;_artworkVideo.UnloadedBehavior=MediaState.Close;_artworkVideo.IsMuted=true;
        _artworkVideo.Stretch=System.Windows.Media.Stretch.UniformToFill;
        _artworkVideo.MediaEnded+=(_,_)=>{if(_videoPlaying&&!_disposed){_artworkVideo.Position=TimeSpan.Zero;_artworkVideo.Play();}};
        _artworkVideo.MediaFailed+=(_,e)=>{_failedVideo=_videoPath;StopArtworkVideo();App.Log("Artwork video: "+e.ErrorException.Message);};
    }
    private void UpdateArtworkVideo()
    {
        string? remote=LyricReplica.Core.ArtworkVideos.Resolve(_song.AlbumId);
        string? path=_options.AppleMusicLyrics.EnableDynamicArtwork?_song.ArtworkVideoPath??remote:null;
        if(path!=_requestedVideo)
        {
            StopArtworkVideo();_requestedVideo=path;
            if(!string.IsNullOrEmpty(path)&&path!=_failedVideo&&(path==remote||File.Exists(path)))
            {_videoPath=path;_artworkVideo.Source=new Uri(path);_artworkVideo.Visibility=Visibility.Visible;}
        }
        bool play=_videoPath is not null&&_clock.IsPlaying&&IsVisible&&WindowState!=WindowState.Minimized&&!_options.ReduceMotion;
        if(play==_videoPlaying)return;_videoPlaying=play;
        if(play)_artworkVideo.Play();else _artworkVideo.Pause();
    }
    private void StopArtworkVideo()
    {
        if(_artworkVideo is null)return;
        _artworkVideo.Close();_artworkVideo.Source=null;_artworkVideo.Visibility=Visibility.Collapsed;_videoPlaying=false;_videoPath=_requestedVideo=null;
    }
    private async Task CheckNativeArtworkVideoAsync(string file,string folder)
    {
        var opened=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? oldAlbum=_song.AlbumId,oldVideo=_song.ArtworkVideoPath,remoteAlbum=null;
        string report=Path.Combine(folder,"native-video.json");File.Delete(report);
        RoutedEventHandler onOpened=(_,_)=>opened.TrySetResult(),onEnded=(_,_)=>ended.TrySetResult();
        _artworkVideo.MediaOpened+=onOpened;_artworkVideo.MediaEnded+=onEnded;
        try
        {
            if(Uri.TryCreate(file,UriKind.Absolute,out var uri)&&uri.Scheme==Uri.UriSchemeHttps)
            {
                remoteAlbum=Path.GetFileNameWithoutExtension(uri.AbsolutePath);
                if(LyricReplica.Core.ArtworkVideos.Resolve(remoteAlbum)!=file)throw new ArgumentException("Expected a fixed reference catalog video URL.",nameof(file));
                _song.AlbumId=remoteAlbum;_song.ArtworkVideoPath=null;
            }
            else
            {
                if(!File.Exists(file))throw new FileNotFoundException("Native video fixture is missing.",file);
                _song.ArtworkVideoPath=Path.GetFullPath(file);
            }
            _options.AppleMusicLyrics.EnableDynamicArtwork=true;_clock.Play();UpdateArtworkVideo();
            if(await Task.WhenAny(opened.Task,Task.Delay(10000))!=opened.Task)throw new InvalidOperationException("Windows did not open the cover video within 10 seconds.");
            await Task.Delay(300);if(_artworkVideo.NaturalVideoWidth<=0||_artworkVideo.Position<=TimeSpan.Zero)throw new InvalidOperationException("The native cover video did not advance.");
            _clock.Pause();UpdateArtworkVideo();await Task.Delay(150);var paused=_artworkVideo.Position;await Task.Delay(200);
            if(Math.Abs((_artworkVideo.Position-paused).TotalMilliseconds)>50)throw new InvalidOperationException("The cover video did not pause with playback.");
            if(!_artworkVideo.NaturalDuration.HasTimeSpan)throw new InvalidOperationException("The cover video has no duration.");
            _artworkVideo.Position=_artworkVideo.NaturalDuration.TimeSpan-TimeSpan.FromMilliseconds(100);_clock.Play();UpdateArtworkVideo();
            if(await Task.WhenAny(ended.Task,Task.Delay(10000))!=ended.Task)throw new InvalidOperationException($"The cover video did not reach MediaEnded: position={_artworkVideo.Position}, duration={_artworkVideo.NaturalDuration}.");
            await Task.Delay(300);
            if(_artworkVideo.Position.TotalMilliseconds>2000)throw new InvalidOperationException("The cover video did not loop after MediaEnded.");
            File.WriteAllText(report,System.Text.Json.JsonSerializer.Serialize(new{passed=true,assemblySha256=App.AssemblySha256,width=_artworkVideo.NaturalVideoWidth,height=_artworkVideo.NaturalVideoHeight,pausedPositionMs=paused.TotalMilliseconds,loopedPositionMs=_artworkVideo.Position.TotalMilliseconds,muted=_artworkVideo.IsMuted,fixture=Path.GetFileName(file),source=remoteAlbum is null?"local":"catalogHttps",mediaEndedObserved=true,windowShown=true,desktopInputSent=false}));
        }
        finally{_artworkVideo.MediaOpened-=onOpened;_artworkVideo.MediaEnded-=onEnded;_clock.Pause();_song.ArtworkVideoPath=oldVideo;_song.AlbumId=oldAlbum;StopArtworkVideo();}
    }
}
