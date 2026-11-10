using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using LyricReplica.Core;

namespace LyricReplica.App.Reference;

public sealed class ReferencePageView
{
    public UserControl View{get;}
    public IReadOnlyList<DependencyObject> Elements{get;}
    private readonly double _firstAngle,_secondAngle;
    public static FontFamily AppleTextFont()=>new(new Uri("pack://application:,,,/"),"./Assets/Fonts/#SF Pro Text, Microsoft YaHei UI");
    public static FontFamily FontFor(FontPresets preset,string? custom=null)=>preset switch
    {
        FontPresets.Custom when !string.IsNullOrWhiteSpace(custom)=>new FontFamily(custom),
        FontPresets.SFPro=>new(new Uri("pack://application:,,,/"),"./Assets/Fonts/#SF Pro Display, Microsoft YaHei UI"),
        FontPresets.SpotifyCircular=>new(new Uri("pack://application:,,,/"),"./Assets/Fonts/#Circular Sp UI v3 T, ./Assets/Fonts/#SF Pro Display, Segoe UI, Microsoft YaHei UI"),
        _=>new(new Uri("pack://application:,,,/"),"./Assets/Fonts/#Spotify Mix, ./Assets/Fonts/#SF Pro Display, Segoe UI Variable Display, Segoe UI, Microsoft YaHei UI")
    };
    public ReferencePageView(FontFamily font,bool deterministic=false)
    {
        _firstAngle=deterministic?0:Random.Shared.Next(360);_secondAngle=deterministic?180:Random.Shared.Next(360);
        Application.Current.Resources["VisibilityConverter"]=new VisibilityConverter();
        Application.Current.Resources["Font.Apple.SpotifyMix"]=FontFor(FontPresets.SpotifyMix);
        Application.Current.Resources["Font.Apple.SpotifyCircular"]=FontFor(FontPresets.SpotifyCircular);
        Application.Current.Resources["Font.Apple"]=FontFor(FontPresets.SFPro);
        Application.Current.Resources["Font.Apple.Text"]=AppleTextFont();
        Application.Current.Resources["Font.Menu.Book"]=font;
        foreach(var (key,value) in new[]{("String.Menu.MainWindow.Main.View.Topmost","窗口置顶"),("String.Menu.AppleMusicLyrics.Zoom.In","放大歌词"),("String.Menu.AppleMusicLyrics.Zoom.Out","缩小歌词"),("String.Menu.AppleMusicLyrics.Zoom.Reset","恢复默认缩放"),("String.Menu.AppleMusicLyrics.Blur","歌词虚化"),("String.Menu.AppleMusicLyrics.BoldText","粗体文字"),("String.Menu.AppleMusicLyrics.Translation","显示翻译")})Application.Current.Resources[key]=value;
        foreach(var (key,value) in new[]{("Copy","复制此行"),("CopyTranslation","复制此行翻译"),("CopyLineWithTranslation","复制此行与翻译"),("CopyAllLines","复制全部歌词"),("CopyAllTranslations","复制全部翻译"),("CopyAllLinesWithTranslation","复制全部歌词与翻译"),("Share","分享歌词")})Application.Current.Resources["String.Menu.MainWindow.MainLyricsLine.Menu."+key]=value;
        foreach(var (key,value) in new[]{("String.Page.AppleMusicLyrics.Menu.AddToLibrary","加入收藏"),("String.Page.AppleMusicLyrics.Menu.DeleteFromLibrary","取消收藏"),("String.Page.AppleMusicLyrics.Menu.ShowArtist","查看艺人"),("String.Page.AppleMusicLyrics.Menu.ShowAlbum","查看专辑"),("String.Menu.Track.OpenTrackManagement","播放列表"),("String.Menu.Pages.CopySpotifyURI","复制 Spotify URI"),("String.Menu.Pages.CopySpotifyLink","复制 Spotify 链接"),("String.Menu.Pages.ShowInSpotify","在 Spotify 中显示"),("String.Page.AppleMusicLyrics.Menu.ArtistClick.GoToArtist","前往艺人"),("String.Page.AppleMusicLyrics.Menu.ArtistClick.GoToAlbum","前往专辑"),("String.Page.AppleMusicLyrics.Menu.ArtistClick.GoToPlaylist","前往播放列表")})Application.Current.Resources[key]=value;
        Application.Current.Resources["Brush.Apple.Button.Foreground"]=new SolidColorBrush(Color.FromRgb(255,82,102));
        foreach(var (key,value) in new[]{("Tip1","点击以关闭"),("Tip2","左滑或右滑以最大化"),("Tip3","下滑以最小化"),("Tip4","按下右键滑动以控制歌词位置")})Application.Current.Resources["String.Page.AppleMusicLyrics.MagicButton."+key]=value;
        foreach(string key in new[]{"Apple.Menu","Apple.Music.Artist","Apple.Music.Album"})
            if(!Application.Current.Resources.Contains(key))Application.Current.Resources[key]=Application.Current.Resources["Apple.Music.Playlist"];
        View=(UserControl)Application.LoadComponent(new Uri("/LyricReplica;component/Assets/ReferencePage.xaml",UriKind.Relative));
        View.Background=Brushes.Transparent;
        var nodes=new List<DependencyObject>();var visited=new HashSet<DependencyObject>();
        Walk(View);Elements=nodes;
        Get<MediaElement>("WebImageHumanVideo").Visibility=Visibility.Collapsed;
        Get<Border>("BorderFpsOverlay").Visibility=Visibility.Collapsed;
        Get<Grid>("GridMagicStripFeatureNotice").Visibility=Visibility.Collapsed;
        foreach(var element in nodes.OfType<InfoMarquee>())element.MotionEnabled=true;
        foreach(var element in nodes.OfType<FrameworkElement>().Where(x=>x.Name.Contains("ForMousePoint",StringComparison.Ordinal)))element.IsHitTestVisible=false;
        void Walk(DependencyObject node)
        {
            if(!visited.Add(node))return;nodes.Add(node);
            foreach(object child in LogicalTreeHelper.GetChildren(node))if(child is DependencyObject item)Walk(item);
            if(node is FrameworkElement element&&element.ContextMenu is not null)Walk(element.ContextMenu);
        }
    }
    public T Get<T>(string name) where T:DependencyObject
    {return View.FindName(name) as T??Elements.OfType<FrameworkElement>().FirstOrDefault(e=>e.Name==name) as T??throw new InvalidOperationException($"Reference node {name} ({typeof(T).Name}) was not found.");}
    public void ApplyMobileInfoStyle(int? preset)
    {
        bool ios16=preset==16;
        var artwork=Get<ArtworkImage>("WebImageAlbumSmall");artwork.Width=artwork.Height=preset.HasValue?(ios16?90:108):100;
        Get<ColumnDefinition>("ViewboxSongInfoSmallColumnDefinition1").Width=new GridLength(preset.HasValue?(ios16?110:125):120);
        var artist=Get<TextBlock>("TextBlockVerticalModeArtistName");artist.FontSize=preset.HasValue?(ios16?24.5:22):25;
        artist.Margin=new Thickness(0,preset.HasValue?(ios16?3:4):5,0,0);
        Get<Grid>("DockPanelVerticalModeTrackName").Margin=new Thickness(0,0,0,preset.HasValue?(ios16?-1:-3):0);
        foreach(string name in new[]{"TextBlockVerticalModeTrackName","TextBlockVerticalModeArtistName"})
        {
            var text=Get<TextBlock>(name);
            if(preset.HasValue)text.SetResourceReference(TextBlock.FontFamilyProperty,"Font.Apple.Text");
            else text.ClearValue(TextBlock.FontFamilyProperty);
        }
    }
    public void SetMotion(bool enabled,bool marquee=true)
    {
        foreach(var element in Elements.OfType<InfoMarquee>()){element.MotionEnabled=enabled&&marquee;element.Recalculate();}
        foreach(var element in Elements.OfType<FavoriteButton>())element.MotionEnabled=enabled;
        foreach(var element in Elements.OfType<ImageButton>())element.FeedbackMotionEnabled=enabled;
    }
    public void SetLegacyVisible(bool visible)=>Get<Viewbox>("ViewboxBackground").Visibility=visible?Visibility.Visible:Visibility.Collapsed;
    public void UpdateLegacy(double seconds,bool lightTheme,bool isStatic=false)
    {
        Get<Grid>("GridBackgroundSub1").Opacity=isStatic?1:.7;Get<Grid>("GridBackgroundSub2").Visibility=isStatic?Visibility.Hidden:Visibility.Visible;
        double dpi=VisualTreeHelper.GetDpi(View).PixelsPerInchX;
        Get<BlurEffect>("WebImageBackgroundBlurEffect").Radius=isStatic?160*dpi/192:100;
        Get<Border>("BorderBackgroundFrontBlackMask").Opacity=isStatic?(lightTheme?.2:.35):(lightTheme?.17:.27);
        if(isStatic){Apply(new BackdropLayer(2,2,0,0,0),"GridBackgroundScaleTransform","GridBackgroundTranslateTransform","GridBackgroundRotateTransform");return;}
        double cache=Math.Max(.01,(1.25-dpi/96/4)/2);
        Get<BitmapCache>("BackgroundBitmapCache1").RenderAtScale=cache;Get<BitmapCache>("BackgroundBitmapCache2").RenderAtScale=cache;
        var frame=LegacyBackdrop.At(seconds,_firstAngle,_secondAngle);
        Apply(frame.First,"GridBackgroundScaleTransform","GridBackgroundTranslateTransform","GridBackgroundRotateTransform");
        Apply(frame.Second,"GridBackgroundScaleTransform2","GridBackgroundTranslateTransform2","GridBackgroundRotateTransform2");
        void Apply(BackdropLayer layer,string scaleName,string moveName,string angleName)
        {
            var scale=Get<ScaleTransform>(scaleName);scale.ScaleX=layer.ScaleX;scale.ScaleY=layer.ScaleY;
            var move=Get<TranslateTransform>(moveName);move.X=layer.X;move.Y=layer.Y;Get<RotateTransform>(angleName).Angle=layer.Angle;
        }
    }
}
