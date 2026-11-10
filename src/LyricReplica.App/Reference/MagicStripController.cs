using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LyricReplica.Core;

namespace LyricReplica.App.Reference;

public sealed class MagicStripController : IDisposable
{
    private readonly Border _outer,_bar;
    private readonly Viewbox _glyph;
    private readonly Grid _notice,_inside;
    private readonly SymbolIcon _symbol,_noticeSymbol;
    private readonly TextBlock _caption;
    private long _hoverStarted=-1;
    private readonly Func<long> _now;
    private readonly Func<bool> _maximized,_reduceMotion;
    private readonly Action<MagicAction> _commit;
    private readonly Func<AppleMusicSettings> _settings;
    private readonly Action _save;
    private readonly TextBlock[] _tips;
    private AppleMusicSettings? _tutorialSettings;
    private bool? _tutorialPreference;
    private double _tutorialStart=double.NaN;
    private int _tutorialStage=-1;
    private bool _tutorialInitialized,_disposed;
    private Point _origin;
    private bool _pressed,_secondary;
    private TouchDevice? _touch;
    private MagicAction _action=MagicAction.Close;
    public MagicAction PreviewAction=>_action;
    public bool TutorialRunning=>!double.IsNaN(_tutorialStart);

    public MagicStripController(ReferencePageView page,Func<bool> maximized,Func<bool> reduceMotion,Action<MagicAction> commit,Func<AppleMusicSettings> settings,Action save,Func<long>? now=null)
    {
        _maximized=maximized;_reduceMotion=reduceMotion;_commit=commit;_settings=settings;_save=save;_now=now??(()=>Environment.TickCount64);
        _outer=page.Get<Border>("BorderMagicButtonOutside");_bar=page.Get<Border>("BorderMagicButtonInside");
        _glyph=page.Get<Viewbox>("ViewboxMagicButtonInside");_notice=page.Get<Grid>("GridMagicStripFeatureNotice");
        _symbol=page.Get<SymbolIcon>("SymbolMagicButton");_noticeSymbol=page.Get<SymbolIcon>("SymbolMagicStripFeatureNotice");_caption=page.Get<TextBlock>("TextBlockMagicStripFeatureNotice");
        _inside=page.Get<Grid>("GridMagicButtonInside");
        _tips=Enumerable.Range(1,4).Select(i=>page.Get<TextBlock>("TextBlockMagicButtonTip"+i)).ToArray();
        RefreshNotice();_outer.Cursor=Cursors.None;
        _outer.MouseEnter+=(_,e)=>{Hover(true);MovePointer(e.GetPosition(_inside));};
        _outer.MouseLeave+=(_,_)=>{if(!_pressed)Hover(false);};
        _outer.MouseDown+=(_,e)=>
        {
            if(e.ChangedButton is not (MouseButton.Left or MouseButton.Right))return;
            if(_touch is not null||!_outer.CaptureMouse())return;
            BeginGesture(e.GetPosition(_inside),e.ChangedButton==MouseButton.Right);e.Handled=true;
        };
        _outer.MouseMove+=(_,e)=>MovePointer(e.GetPosition(_inside));
        _outer.MouseUp+=(_,e)=>
        {
            if(!_pressed||e.ChangedButton!=(_secondary?MouseButton.Right:MouseButton.Left))return;
            var action=_action;_pressed=false;_outer.ReleaseMouseCapture();Hover(false);e.Handled=true;_commit(action);
        };
        _outer.LostMouseCapture+=(_,_)=>{if(_pressed){_pressed=false;Hover(false);}};
        _outer.TouchDown+=(_,e)=>{if(_pressed||_touch is not null||!_outer.CaptureTouch(e.TouchDevice))return;_touch=e.TouchDevice;Hover(true);BeginGesture(e.GetTouchPoint(_inside).Position,false);e.Handled=true;};
        _outer.TouchMove+=(_,e)=>{if(e.TouchDevice==_touch){MovePointer(e.GetTouchPoint(_inside).Position);e.Handled=true;}};
        _outer.TouchUp+=(_,e)=>{if(e.TouchDevice!=_touch)return;var action=_action;_pressed=false;_touch=null;_outer.ReleaseTouchCapture(e.TouchDevice);Hover(false);e.Handled=true;_commit(action);};
        _outer.LostTouchCapture+=(_,e)=>{if(e.TouchDevice==_touch){_touch=null;_pressed=false;Hover(false);}};
        page.Get<Grid>("GridMagicButton").MouseDown+=(_,e)=>{if(e.ChangedButton==MouseButton.Left&&e.ClickCount>=2){_commit(_maximized()?MagicAction.Restore:MagicAction.Maximize);e.Handled=true;}};
    }
    public void Preview(bool hovered,MagicAction action)
    {Hover(hovered);SetAction(action);}
    private void RefreshNotice()
    {
        bool visible=_settings().MagicStripStatus;_notice.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
        if(!visible){_notice.BeginAnimation(UIElement.OpacityProperty,null);_notice.Opacity=0;}
    }
    public void BeginTutorial(double nowMs)
    {
        if(_disposed)return;
        StopTutorial();var settings=_settings();bool persist=!settings.MagicStripTutorial.HasValue;
        bool enabled=settings.BeginMagicStripTutorial();_tutorialSettings=settings;_tutorialPreference=settings.MagicStripTutorial;_tutorialInitialized=true;
        if(persist)_save();
        if(enabled)_tutorialStart=nowMs;
        RefreshNotice();
    }
    public void AdvanceTutorial(double nowMs)
    {
        if(_disposed)return;
        var settings=_settings();
        if(_tutorialInitialized&&(_tutorialSettings!=settings||_tutorialPreference!=settings.MagicStripTutorial))BeginTutorial(nowMs);
        RefreshNotice();
        if(!TutorialRunning)return;
        double elapsed=nowMs-_tutorialStart-1500;
        if(elapsed<0)return;
        if(elapsed>=13450){_tips[3].Visibility=Visibility.Collapsed;_tutorialStart=double.NaN;return;}
        int index=Math.Min(3,(int)(elapsed/3350));bool entering=elapsed-index*3350<3000;
        int stage=index*2+(entering?0:1);
        if(stage==_tutorialStage)return;_tutorialStage=stage;
        for(int i=0;i<_tips.Length;i++)if(i!=index)_tips[i].Visibility=Visibility.Collapsed;
        var tip=_tips[index];tip.Visibility=Visibility.Visible;
        var ease=new BackEase{Amplitude=entering?.5:index==3?.5:.3};
        Animate(tip,TextBlock.FontSizeProperty,entering?18:.01,entering?700:400,new BackEase{Amplitude=entering?.5:.3});
        Animate(tip,UIElement.OpacityProperty,entering?1:0,entering?700:400);
        var text=new FormattedText(tip.Text,CultureInfo.CurrentUICulture,tip.FlowDirection,new Typeface(tip.FontFamily,tip.FontStyle,tip.FontWeight,tip.FontStretch),18,Brushes.White,VisualTreeHelper.GetDpi(tip).PixelsPerDip);
        int duration=entering?700:index==3?500:400;
        Animate(_bar,FrameworkElement.WidthProperty,entering?text.Width+35:40,duration,ease);
        Animate(_bar,FrameworkElement.HeightProperty,entering?text.Height+15:8,duration,ease);
        if(_reduceMotion()){_bar.BeginAnimation(Border.CornerRadiusProperty,null);_bar.CornerRadius=new CornerRadius(entering?(text.Height+15)/2:4);}
        else _bar.BeginAnimation(Border.CornerRadiusProperty,new RadiusAnimation(_bar.CornerRadius.TopLeft,entering?(text.Height+15)/2:4,ease){Duration=TimeSpan.FromMilliseconds(duration)});
    }
    private void StopTutorial()
    {
        _tutorialStart=double.NaN;_tutorialStage=-1;
        foreach(var tip in _tips){tip.BeginAnimation(TextBlock.FontSizeProperty,null);tip.BeginAnimation(UIElement.OpacityProperty,null);tip.FontSize=.01;tip.Opacity=0;tip.Visibility=Visibility.Collapsed;}
        Hover(false);
    }
    public void Dispose()
    {
        if(_disposed)return;StopTutorial();_disposed=true;
        _pressed=false;if(_outer.IsMouseCaptured)_outer.ReleaseMouseCapture();if(_touch is { } touch){_touch=null;_outer.ReleaseTouchCapture(touch);}
        foreach(var property in new[]{FrameworkElement.WidthProperty,FrameworkElement.HeightProperty,Border.CornerRadiusProperty,UIElement.OpacityProperty})_bar.BeginAnimation(property,null);
        _glyph.BeginAnimation(UIElement.OpacityProperty,null);_notice.BeginAnimation(UIElement.OpacityProperty,null);
        _bar.BeginAnimation(FrameworkElement.MarginProperty,null);_hoverStarted=-1;
    }
    private void Hover(bool hovered)
    {
        if(hovered)SetAction(MagicAction.Close);
        if(!hovered){_hoverStarted=-1;Animate(_notice,UIElement.OpacityProperty,0,150);Translate(0,0,500,.25);}
        if(TutorialRunning){if(hovered)_hoverStarted=_now();return;}
        var easing=new BackEase{Amplitude=hovered?.7:.5};
        Animate(_bar,FrameworkElement.WidthProperty,hovered?30:40,hovered?350:400,easing);
        Animate(_bar,FrameworkElement.HeightProperty,hovered?30:8,hovered?350:400,easing);
        if(_reduceMotion()){_bar.BeginAnimation(Border.CornerRadiusProperty,null);_bar.CornerRadius=new CornerRadius(hovered?8:4);}
        else _bar.BeginAnimation(Border.CornerRadiusProperty,new RadiusAnimation(_bar.CornerRadius.TopLeft,hovered?8:4,easing){Duration=TimeSpan.FromMilliseconds(hovered?350:400)});
        Animate(_bar,UIElement.OpacityProperty,hovered?.4:.25,hovered?350:400);
        Animate(_glyph,UIElement.OpacityProperty,hovered?.7:0,hovered?350:400);
        if(hovered)_hoverStarted=_now();
    }
    internal void MovePointer(Point point)
    {
        if(_disposed)return;
        if(_pressed)SetAction(MagicGesture.Select(point.X-_origin.X,point.Y-_origin.Y,_secondary,_maximized()));
        if(_hoverStarted<0)return;
        long elapsed=_now()-_hoverStarted;
        int remaining=elapsed<=350?(int)Math.Max(0,350-elapsed):0;
        Translate((point.X-_inside.ActualWidth/2)/1.5,(point.Y-_inside.ActualHeight/2)/1.5,remaining,.7);
    }
    internal void BeginGesture(Point point,bool secondary)
    {_origin=point;_secondary=secondary;_pressed=true;SetAction(secondary?MagicAction.None:MagicAction.Close);if(_settings().MagicStripStatus)Animate(_notice,UIElement.OpacityProperty,1,80);}
    private void SetAction(MagicAction action)
    {
        _action=action;
        var (symbol,caption)=action switch
        {
            MagicAction.Close=>("Dismiss16","关闭"),MagicAction.Maximize=>("Maximize16","最大化"),MagicAction.Restore=>("SquareMultiple16","还原窗口"),MagicAction.Minimize=>("Subtract16","最小化"),
            MagicAction.Top=>("PaddingTop20","歌词顶部"),MagicAction.Bottom=>("PaddingDown20","歌词底部"),MagicAction.Current=>("Location16","当前歌词"),_=>("CircleOff16","取消")
        };
        _symbol.Symbol=action is MagicAction.None or MagicAction.Cancel?"Empty":symbol;_noticeSymbol.Symbol=symbol;_caption.Text=caption;
    }
    private void Translate(double x,double y,int milliseconds,double amplitude)
    {
        var target=new Thickness(x,y,-x,-y);
        if(_reduceMotion()||milliseconds<=0){_bar.BeginAnimation(FrameworkElement.MarginProperty,null);_bar.Margin=target;return;}
        _bar.BeginAnimation(FrameworkElement.MarginProperty,new ThicknessAnimation(_bar.Margin,target,TimeSpan.FromMilliseconds(milliseconds)){EasingFunction=new BackEase{Amplitude=amplitude}});
    }
    private void Animate(UIElement target,DependencyProperty property,double value,int milliseconds,IEasingFunction? easing=null)
    {
        if(_reduceMotion()){target.BeginAnimation(property,null);target.SetValue(property,value);}
        else target.BeginAnimation(property,new DoubleAnimation(value,TimeSpan.FromMilliseconds(milliseconds)){EasingFunction=easing});
    }
    private sealed class RadiusAnimation(double from,double to,IEasingFunction easing):AnimationTimeline
    {
        public override Type TargetPropertyType=>typeof(CornerRadius);
        protected override Freezable CreateInstanceCore()=>new RadiusAnimation(from,to,easing);
        public override object GetCurrentValue(object origin,object destination,AnimationClock clock)=>new CornerRadius(Math.Max(0,from+(to-from)*easing.Ease(clock.CurrentProgress??0)));
    }
}
