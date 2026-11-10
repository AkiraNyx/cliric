using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LyricReplica.App.Reference;

internal static class InteractionFeedback
{
    internal static void Opacity(UIElement element,double to,bool motion)
    {
        if(motion)element.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(element.Opacity,to,TimeSpan.FromMilliseconds(300)));
        else{element.BeginAnimation(UIElement.OpacityProperty,null);element.Opacity=to;}
    }
    internal static void Scale(FrameworkElement element,double from,double to,int duration,bool motion)
    {
        var scale=new ScaleTransform(to,to);element.RenderTransform=scale;element.RenderTransformOrigin=new Point(.5,.5);
        if(!motion)return;
        var animation=new DoubleAnimation(from,to,TimeSpan.FromMilliseconds(duration)){EasingFunction=new BackEase{Amplitude=1}};
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,animation);scale.BeginAnimation(ScaleTransform.ScaleYProperty,animation);
    }
}

internal sealed class RowFeedback
{
    private readonly FrameworkElement _scale;
    private readonly Border _background;
    private readonly Func<bool> _motion,_allowed;
    private bool _leftPressed,_pressed;
    internal bool Hovered{get;private set;}
    internal RowFeedback(FrameworkElement host,FrameworkElement scale,Border background,Func<bool> motion,Func<bool> allowed)
    {
        _scale=scale;_background=background;_motion=motion;_allowed=allowed;background.Visibility=Visibility.Visible;
        host.MouseEnter+=(_,e)=>{if(e.StylusDevice is null&&_allowed()){Hovered=true;InteractionFeedback.Opacity(_background,.08,_motion());}};
        host.MouseLeave+=(_,_)=>{if(host.ContextMenu?.IsOpen!=true)Leave();if(_leftPressed)Release();};
        host.PreviewMouseDown+=(_,e)=>
        {
            if(e.ChangedButton is not(MouseButton.Left or MouseButton.Right))return;
            _pressed=true;
            _leftPressed=e.ChangedButton==MouseButton.Left;
            if(e.StylusDevice is not null)InteractionFeedback.Opacity(_background,.08,_motion());
            InteractionFeedback.Scale(_scale,1,.95,450,_motion());
        };
        host.PreviewMouseUp+=(_,e)=>{if(_pressed&&(_leftPressed||host.ContextMenu is null)){Release();if(e.StylusDevice is not null)Leave();}};
        if(host.ContextMenu is { } menu)menu.Closed+=(_,_)=>{Leave();Release();};
    }
    private void Leave(){Hovered=false;InteractionFeedback.Opacity(_background,0,_motion());}
    private void Release(){_pressed=_leftPressed=false;InteractionFeedback.Scale(_scale,.95,1,450,_motion());}
}
