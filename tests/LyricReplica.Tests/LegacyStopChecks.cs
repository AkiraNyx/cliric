using System.Text.Json;
using LyricReplica.Core;

static class LegacyStopChecks
{
    public static void Run(Action<bool,string> check)
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/legacy-stop-original.json")));
        check(fixture.RootElement.GetProperty("publicStartCalled").GetBoolean()&&fixture.RootElement.GetProperty("publicStopCalled").GetBoolean(),"legacy fixture calls original public Start and Stop");
        foreach(var item in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var word=new LyricWord{Text=item.GetProperty("text").GetString()!,StartMs=1000,DurationMs=item.GetProperty("duration").GetDouble()};
            double requested=item.GetProperty("stopRequestedMs").GetDouble(),stop=requested<0?double.PositiveInfinity:word.StartMs+requested,initialY=item.GetProperty("initialY").GetDouble();
            var native=item.GetProperty("snapshots")[0];
            for(int index=0;index<2;index++)
            {
                bool grown=native.GetProperty("moves")[index].ValueKind!=JsonValueKind.Null;
                var pose=Motion.LegacyGlyph(1850,word,index,2,41,true,initialY:initialY,stoppedAtMs:stop);
                check((pose.Scale>1)==grown&&(pose.Y<initialY)==grown,"legacy cancellation matches original attached character clocks, stop="+requested+" index="+index);
                var settled=Motion.LegacyGlyph(6000,word,index,2,41,true,initialY:initialY,initialScale:1.08,stoppedAtMs:stop);
                check(Math.Abs(settled.Scale-1)<1e-9,"legacy return worker runs even for a character whose growth was canceled");
                check(Math.Abs(settled.Y-(grown?-2.75:initialY))<1e-9,"legacy Stop leaves unstarted Y alone and keeps started Y clocks running");
            }
            foreach(var snapshot in item.GetProperty("snapshots").EnumerateArray())
            {
                double time=word.StartMs+snapshot.GetProperty("elapsedMs").GetDouble();
                check((Motion.WordGlow(time,word,stop)>0)==snapshot.GetProperty("hasGlow").GetBoolean(),"legacy glow survives Stop until its original scheduled fade, stop="+requested);
            }
        }
        var shortWord=new LyricWord{Text="ab",StartMs=1000,DurationMs=2000};
        check(Motion.GlyphDelay(shortWord,0,2,MotionPreset.Lyricify)==50&&Motion.GlyphDelay(shortWord,1,2,MotionPreset.Lyricify)==420,"legacy character delays retain original integer waits");
        check(Motion.WordGlow(1850,shortWord,1250)==.43&&Motion.WordGlow(2450,shortWord,1250)==0,"stopped legacy glow holds then clears with zero duration");
        check(Motion.WordGlow(2550,shortWord,2500)>0,"stopping after fade begins keeps the already attached fade clock");
    }
}
