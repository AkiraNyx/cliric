using System.Text.Json;
using LyricReplica.Core;

static class EarlyFinishChecks
{
    public static void Run(Action<bool,string> check)
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/early-finish-original.json")));
        check(fixture.RootElement.GetProperty("originalRunCalled").GetBoolean(),"early-finish fixture invokes original Run");
        foreach(var item in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var words=new List<LyricWord>{new(){Text="hello ",StartMs=1000,DurationMs=200},new(){Text="world",StartMs=item.GetProperty("lastWordStart").GetDouble(),DurationMs=1000}};
            var state=new WordRunState();bool style=item.GetProperty("ios164").GetBoolean();double next=item.GetProperty("nextStart").GetDouble();
            double initial=item.GetProperty("initialPosition").GetDouble(),observed=item.GetProperty("observedPosition").GetDouble();
            state.Advance(initial,words,next,style,1);state.Advance(observed,words,next,style,1);
            check(state.CurrentWord==item.GetProperty("selectedWord").GetInt32()&&state.Finishing==item.GetProperty("forceFinish").GetBoolean(),"original scheduler selection and early finish: "+item);
            if(state.Finishing)
            {
                check(Math.Abs(state.FinishDurationMs-item.GetProperty("durationMs").GetDouble())<1e-9,"original forced sweep duration includes the zero-duration late case");
                check(state.FinishProgress(.3,observed+state.FinishDurationMs)==1,"forced sweep reaches the whole padded strip endpoint");
                double held=state.FinishProgress(.3,observed+state.FinishDurationMs/2);state.Advance(observed,words,next,style,1);
                check(state.FinishProgress(.3,observed+state.FinishDurationMs/2)==held,"pausing cannot drift or restart an early finish");
            }
            if(item.GetProperty("floating").GetBoolean()&&state.Finishing&&state.CurrentWord>=0)
                for(int index=state.CurrentWord;index<words.Count;index++)
                {
                    var plan=item.GetProperty("floatPlans")[index];
                    check(plan.GetProperty("durationMs").GetDouble()==1337.6&&plan.GetProperty("to").GetDouble()==-2.75,"original forced finish returns remaining lowered words with its separate float plan");
                    foreach(var point in plan.GetProperty("samples").EnumerateArray())
                        check(Math.Abs(FloatingMotion.EarlyReturn(plan.GetProperty("durationMs").GetDouble()*point.GetProperty("fraction").GetDouble(),plan.GetProperty("fromForSampling").GetDouble(),41)-point.GetProperty("value").GetDouble())<1e-6,"original forced-finish float curve sample");
                }
            state.Advance(1000,words,next,style,2);check(!state.Finishing&&state.CurrentWord==0,"seek generation resets the original worker selection");
            var snapshot=state.Snapshot();snapshot.Advance(1900,words,next,style,3);
            check(state.CurrentWord==0&&!state.Finishing,"reflow snapshots do not mutate the old word worker state");
        }
        var noNext=new WordRunState();noNext.Advance(5000,[new(){StartMs=1000,DurationMs=9000}],-1,false,1);
        check(!noNext.Finishing,"last page row has no forced next-line finish");
        var rows=new List<DisplayLine>{new(new(){Text="a",EndMs=2000},[],1000,false),new(new(){Text="b",EndMs=4000},[],3000,false),new(new(){Text="c",EndMs=6000},[],5000,false)};
        check(WordSweep.NextLineStart(rows,0)==3000&&WordSweep.NextLineStart(rows,2)==-1,"next-start selection follows the next main row or the final sentinel");
        rows[0]=rows[0] with{PreparedEndMs=3500};check(WordSweep.NextLineStart(rows,0)==5000,"a deeply overlapping main row skips the next row");
        rows[0]=rows[0] with{Backgrounds=[new(){Background=true}]};check(WordSweep.NextLineStart(rows,0)==-1,"a main row with backing vocals does not use the overlap skip branch");
    }
}
