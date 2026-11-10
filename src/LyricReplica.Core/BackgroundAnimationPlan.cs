namespace LyricReplica.Core;

public readonly record struct BackgroundFrame(double Opacity,double Scale,double Y,double ParentOffsetY);

public sealed record BackgroundAnimationPlan(bool Playing,bool Ios,int EntryMode,bool Above,bool UseBackEase,double ScrollGap,
    double FromOpacity,double FromScale,double Height,double Lift,double OpacityDurationMs,double ScaleDurationMs,double MovementDurationMs)
{
    public BackgroundFrame At(double elapsedMs)
    {
        double opacityProgress=Progress(OpacityDurationMs),scaleProgress=Progress(ScaleDurationMs);
        if(Ios){opacityProgress=BackgroundSpring.Ease(Playing,opacityProgress);scaleProgress=BackgroundSpring.Ease(Playing,scaleProgress);}
        else scaleProgress=Playing&&EntryMode!=2?1-Math.Pow(1-scaleProgress,3.6):Exponential(scaleProgress,Playing?5.6:5);
        double opacity=FromOpacity+((Playing?.66:0)-FromOpacity)*opacityProgress;
        double scaleFrom=!Ios&&Playing&&EntryMode!=2?.8:FromScale;
        double scaleTo=Playing?1:Ios||EntryMode!=2?.9:0;
        bool back=UseBackEase&&(Ios||Playing||EntryMode!=2);
        double movement=Ios?Motion.Scroll(Progress(MovementDurationMs),back?ScrollGap:null):back?Motion.LegacyBackScroll(Progress(MovementDurationMs)):Motion.LegacyScroll(Progress(MovementDurationMs));
        double fromY=Playing?(EntryMode==0?(Above?Height:-Height):Lift):Height,toY=Playing?0:Height+Lift;
        return new(Math.Clamp(opacity,0,1),scaleFrom+(scaleTo-scaleFrom)*scaleProgress,fromY+(toY-fromY)*movement,Above?(Playing?-Height:Height)*(1-movement):0);
        double Progress(double duration)=>duration<=0?1:Math.Clamp(elapsedMs/duration,0,1);
        static double Exponential(double progress,double exponent)=>(1-Math.Exp(-exponent*progress))/(1-Math.Exp(-exponent));
    }
}

public static class BackgroundMotion
{
    public static BackgroundAnimationPlan Start(LineFrame from,bool playing,double height,bool above,double scale,AppleMusicSettings settings,
        bool timedPage=true,bool useBackEase=false,double scrollGap=.2,bool reduceMotion=false)
    {
        bool ios=settings.InterLineAnimationPreset==InterLineAnimationPresetTypes.iOS;int mode=settings.BackgroundVocalsEnterAnimation;
        double speed=settings.AnimationSpeed,scroll=ios?Math.Round(Motion.ScrollParameters().Duration*1000):timedPage?500:620;
        double spring=Math.Round(BackgroundSpring.Duration(playing)*1000*speed);
        double opacity=ios?spring:playing?400:mode==2?300:150;
        double scaling=ios?spring:scroll*speed*(playing&&mode!=2?1.8:1);
        double movement=Math.Truncate(scroll*speed*(useBackEase?(ios?Motion.ScrollParameters(scrollGap).Duration/Motion.ScrollParameters().Duration:2):1));
        double lift=Math.Min(Math.Max(height*.18,6*scale),20*scale)*(above?1:-1);
        return new(playing,ios,mode,above,useBackEase,scrollGap,from.RootOpacity,from.RootScale,height,lift,Duration(opacity),Duration(scaling),Duration(movement));
        double Duration(double value)=>reduceMotion?0:TimeSpan.FromMilliseconds(value).TotalMilliseconds;
    }
}
