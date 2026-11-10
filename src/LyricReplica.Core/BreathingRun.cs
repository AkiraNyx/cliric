namespace LyricReplica.Core;

public sealed class BreathingRun(Func<bool>? chooseTremendous=null)
{
    private readonly record struct Tween(double From,double To,double Started,double Duration,bool Quadratic=false)
    {
        public double At(double now){double p=Duration<=0?1:Math.Clamp((now-Started)/Duration,0,1);if(Quadratic)p=BreathingMotion.Quadratic(p);return From+(To-From)*p;}
        public static Tween Hold(double value)=>new(value,value,0,0);
    }
    private Tween _scale=Tween.Hold(1),_opacity=Tween.Hold(0);
    private readonly Tween[] _dots=[Tween.Hold(.15),Tween.Hold(.15),Tween.Hold(.15)];
    private double _requestedStart,_startedClock,_visibleClock,_exitClock,_exitFrom;
    private bool _delayed,_adjusted,_secondEntry,_thirdEntry,_tremendous,_visible;
    public bool Pending{get;private set;}
    public bool IsRunning{get;private set;}
    public bool IsExiting{get;private set;}
    public double StartMs{get;private set;}
    public double EndMs{get;private set;}
    public int Cycles{get;private set;}
    public int PeriodMs{get;private set;}
    public int DotPeriodMs{get;private set;}
    public int PulseIndex{get;private set;}
    public int DotIndex{get;private set;}

    public void Start(double start,double end,bool delayed,double now)
    {
        Stop();
        if(!double.IsFinite(start)||!double.IsFinite(end)||!double.IsFinite(now)||end-start<=(delayed?1000:500)+2360)return;
        StartMs=_requestedStart=Math.Truncate(start);EndMs=Math.Truncate(end);_startedClock=now;_delayed=delayed;
        Pending=IsRunning=true;_adjusted=_secondEntry=_thirdEntry=false;
        _scale=Tween.Hold(1);_opacity=Tween.Hold(0);
        for(int i=0;i<3;i++)_dots[i]=Tween.Hold(.15);
    }
    public void Tick(double position,bool playing,double now)
    {
        if(Pending)
        {
            if(now-_startedClock<500)return;
            if(!_adjusted){StartMs=_requestedStart+500;_adjusted=true;}
            if(_delayed&&now-_startedClock<1000)return;
            StartMs=_requestedStart+(_delayed?1000:500);
            double duration=EndMs-StartMs;
            Cycles=Math.Max(1,(int)((duration-1260)/5500+.25));PeriodMs=(int)((duration-1260)/Cycles);DotPeriodMs=(int)((duration-2360)/3);
            Pending=false;_visible=true;_visibleClock=now;_opacity=Tween.Hold(1);
            _dots[0]=new(0,.15,now,750);_dots[1]=_dots[2]=Tween.Hold(0);
            return;
        }
        AdvanceEntry(now);
        if(!IsRunning||!playing)return;
        if(!double.IsFinite(position)||position<StartMs-500||position>EndMs){Stop();return;}
        if(DotIndex<3&&position>DotIndex*DotPeriodMs+1200+StartMs)
        {int index=DotIndex++;_dots[index]=new(_dots[index].At(now),1,now,TimeSpan.FromMilliseconds(DotPeriodMs*.8).TotalMilliseconds);}
        if(position-StartMs>=Cycles*PeriodMs)
        {
            _exitClock=now;_exitFrom=_scale.At(now);_tremendous=chooseTremendous?.Invoke()==true;
            IsRunning=false;IsExiting=true;return;
        }
        if(PulseIndex<Cycles*2&&position>PulseIndex*PeriodMs/2+StartMs)
        {
            PulseIndex++;double from=PulseIndex==1?.8:_scale.At(now),to=PulseIndex%2==1?1.1:.8;
            _scale=new(from,to,now,TimeSpan.FromMilliseconds(PeriodMs/2.05).TotalMilliseconds,true);
        }
    }
    public BreathingFrame FrameAt(double now,bool reduceMotion=false)
    {
        AdvanceEntry(now);
        double scale=_scale.At(now),opacity=_opacity.At(now);
        if(IsExiting)
        {
            var exit=BreathingMotion.Exit(now-_exitClock,_exitFrom,_tremendous);scale=exit.Scale;opacity=exit.Opacity;
            if(exit.Finished){Stop();_scale=Tween.Hold(scale);_opacity=Tween.Hold(opacity);}
        }
        return new(_visible,reduceMotion?1:scale,opacity,_dots[0].At(now),_dots[1].At(now),_dots[2].At(now));
    }
    private void AdvanceEntry(double now)
    {
        if(!_visible)return;
        if(!_secondEntry&&now-_visibleClock>=84)
        {
            _secondEntry=true;
            if(DotIndex<2)_dots[1]=new(0,.15,_visibleClock+84,750);
            else _thirdEntry=true;
        }
        if(_secondEntry&&!_thirdEntry&&now-_visibleClock>=168)
        {_thirdEntry=true;if(DotIndex<3)_dots[2]=new(0,.15,_visibleClock+168,750);}
    }
    public void Stop()
    {
        Pending=IsRunning=IsExiting=_visible=false;
        Cycles=PeriodMs=DotPeriodMs=PulseIndex=DotIndex=0;
    }
}
