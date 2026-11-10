using System.Text.Json;
using LyricReplica.Core;

static class BreathingRunChecks
{
    public static void Run(Action<bool,string> check)
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/breathing-original.json")));
        check(fixture.RootElement.GetProperty("originalStartTickAndExitHelpersCalled").GetBoolean(),"breathing fixture invokes original Start, timer and exit helpers");
        foreach(var item in fixture.RootElement.GetProperty("starts").EnumerateArray())
        {
            bool delayed=item.GetProperty("delayed").GetBoolean();double visible=delayed?1000:500;
            var run=new BreathingRun();run.Start(1000,13000,delayed,0);run.Tick(1000,false,499);
            check(run.IsRunning&&run.Pending&&!run.FrameAt(499).Visible,"breathing Start waits on wall time while playback is paused");
            run.Tick(1000,false,500);if(delayed)run.Tick(1000,false,1000);
            check(run.StartMs==item.GetProperty("adjustedStart").GetDouble()&&run.Cycles==item.GetProperty("cycles").GetInt32()&&run.PeriodMs==item.GetProperty("period").GetInt32()&&run.DotPeriodMs==item.GetProperty("dotPeriod").GetInt32(),"breathing adjusted times and integer cycle plan match original Start");
            for(int dot=0;dot<3;dot++)
                foreach(var sample in item.GetProperty("entry")[dot].GetProperty("samples").EnumerateArray())
                {
                    var frame=run.FrameAt(visible+dot*84+sample.GetProperty("elapsedMs").GetDouble());
                    double value=dot==0?frame.First:dot==1?frame.Second:frame.Third;
                    check(Math.Abs(value-sample.GetProperty("value").GetDouble())<1e-8,"paused initial dot fade matches original wall-clock plan: delayed="+delayed+" dot="+dot+" elapsed="+sample.GetProperty("elapsedMs").GetDouble()+" actual="+value+" expected="+sample.GetProperty("value").GetDouble());
                }
            check(run.PulseIndex==0&&run.DotIndex==0,"paused startup does not dispatch a pulse or upgrade a dot");
        }
        var dispatch=fixture.RootElement.GetProperty("dispatch");
        foreach(var sample in dispatch.GetProperty("pulse1").GetProperty("samples").EnumerateArray())
        {
            var run=PulseRun();double elapsed=sample.GetProperty("elapsedMs").GetDouble();
            check(Math.Abs(run.FrameAt(550+elapsed).Scale-sample.GetProperty("value").GetDouble())<1e-8,"first pulse matches original From0.8/Quadratic timing");
        }
        var paused=PulseRun();double scaleBefore=paused.FrameAt(600).Scale;paused.Tick(2801,false,650);
        check(paused.PulseIndex==1&&paused.DotIndex==0&&paused.FrameAt(750).Scale>scaleBefore,"paused tick gates new dispatch while the existing pulse keeps running");
        foreach(var sample in dispatch.GetProperty("pulse2").GetProperty("samples").EnumerateArray())
        {
            var run=PulseRun();double from=run.FrameAt(850).Scale;run.Tick(8000,true,850);
            double progress=(sample.GetProperty("value").GetDouble()-1.03)/(.8-1.03);
            check(run.PulseIndex==2&&Math.Abs(run.FrameAt(850+sample.GetProperty("elapsedMs").GetDouble()).Scale-(from+(.8-from)*progress))<1e-8,"late tick advances one pulse and uses its current scale snapshot");
        }
        foreach(var exit in fixture.RootElement.GetProperty("exits").EnumerateArray())
        {
            bool huge=exit.GetProperty("huge").GetBoolean();
            foreach(string stage in new[]{"expand","shrink"})
                foreach(var sample in exit.GetProperty(stage).GetProperty("samples").EnumerateArray())
                {
                    var frame=BreathingMotion.Exit((stage=="expand"?0:975)+sample.GetProperty("elapsedMs").GetDouble(),1.04,huge);
                    check(Math.Abs(frame.Scale-sample.GetProperty("value").GetDouble())<1e-8,"exit scale matches original "+stage+" huge="+huge);
                }
            if(huge)
            {
                var bounce=exit.GetProperty("bounce");double interrupted=bounce.GetProperty("samples").EnumerateArray().Single(s=>Math.Abs(s.GetProperty("elapsedMs").GetDouble()-100)<1e-6).GetProperty("value").GetDouble();
                foreach(var sample in bounce.GetProperty("samples").EnumerateArray().Where(s=>s.GetProperty("elapsedMs").GetDouble()<=100))
                    check(Math.Abs(BreathingMotion.Exit(1375+sample.GetProperty("elapsedMs").GetDouble(),1.04,true).Scale-sample.GetProperty("value").GetDouble())<1e-8,"huge bounce follows its original first 100ms before replacement");
                var final=exit.GetProperty("final");
                foreach(var sample in final.GetProperty("samples").EnumerateArray())
                {
                    double progress=(sample.GetProperty("value").GetDouble()-final.GetProperty("defaultFrom").GetDouble())/(.5-final.GetProperty("defaultFrom").GetDouble());
                    check(Math.Abs(BreathingMotion.Exit(1475+sample.GetProperty("elapsedMs").GetDouble(),1.04,true).Scale-(interrupted+(.5-interrupted)*progress))<1e-8,"huge last bounce starts at the interrupted original curve snapshot");
                }
            }
            var fade=exit.GetProperty(huge?"lastFade":"fade");
            foreach(var sample in fade.GetProperty("samples").EnumerateArray())
                check(Math.Abs(BreathingMotion.Exit((huge?1475:975)+sample.GetProperty("elapsedMs").GetDouble(),1.04,huge).Opacity-sample.GetProperty("value").GetDouble())<1e-8,"exit opacity matches the original linear fade");
            int choices=0;var run=new BreathingRun(()=>{choices++;return huge;});run.Start(1000,13000,true,0);run.Tick(2000,false,1000);run.Tick(11740,false,1050);
            check(choices==0&&run.IsRunning,"tremendous choice is not sampled on a paused tick");run.Tick(11740,true,1100);
            check(choices==1&&run.IsExiting&&!run.IsRunning,"tremendous choice is sampled once at exit dispatch");
            check(Math.Abs(run.FrameAt(2000).Scale-(huge?5:1.13))<1e-8,"dispatched exit expansion continues without further player ticks");
            check(!run.FrameAt(1100+(huge?1675:1275)).Visible&&!run.IsExiting,"exit completes at its published wait sequence while playback is paused");
        }
        var invalid=new BreathingRun();invalid.Start(double.NaN,13000,false,0);check(!invalid.IsRunning&&!invalid.FrameAt(0).Visible,"non-finite imported breathing ranges cannot start an animation");
        static BreathingRun PulseRun(){var run=new BreathingRun();run.Start(1000,14760,false,0);run.Tick(1500,false,500);run.Tick(1600,true,550);return run;}
    }
}
