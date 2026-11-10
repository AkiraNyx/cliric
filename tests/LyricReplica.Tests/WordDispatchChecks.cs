using System.Text.Json;
using LyricReplica.Core;

static class WordDispatchChecks
{
    public static void Run(Action<bool,string> check)
    {
        foreach(string name in new[]{"word-dispatch-original.json","word-dispatch-mixed-settled-original.json","word-dispatch-edges-original.json"})
        {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures",name)));
        check(fixture.RootElement.GetProperty("originalRunCalled").GetBoolean()&&fixture.RootElement.GetProperty("wordStartCalled").GetBoolean(),"word-dispatch fixture invokes original Run and child Start");
        foreach(var sample in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var prefixWidths=new Dictionary<string,double>();
            var words=sample.GetProperty("words").EnumerateArray().Select((word,index)=>{
                string prefix="";var syllables=new List<Syllable>();
                foreach(var syllable in word.GetProperty("syllables").EnumerateArray()){prefix+=syllable.GetProperty("text").GetString()!;prefixWidths[prefix]=syllable.GetProperty("prefixInk").GetDouble();syllables.Add(new(){Text=syllable.GetProperty("text").GetString()!,StartMs=syllable.GetProperty("start").GetDouble(),DurationMs=syllable.GetProperty("duration").GetDouble()});}
                return new MeasuredWord(new(){Text=word.GetProperty("text").GetString()!,StartMs=word.GetProperty("start").GetDouble(),DurationMs=word.GetProperty("duration").GetDouble(),Syllables=syllables},index,new(word.GetProperty("width").GetDouble(),word.GetProperty("ink").GetDouble(),50));
            }).ToArray();
            var layout=new WrappedLine(words,words.Sum(word=>word.Size.Width));double position=sample.GetProperty("position").GetDouble();int selected=sample.GetProperty("selected").GetInt32();
            var state=new WordRunState();state.Advance(position,words.Select(word=>word.Word).ToArray(),-1,true,1);
            for(int index=0;index<words.Length;index++)
            {
                var dispatch=state.DispatchFor(index);bool started=sample.GetProperty("shineRunning")[index].GetBoolean();
                check((dispatch?.ShineStarted==true)==started,"original Run child Start agrees for every selected and passed word: "+name+" position="+position+" index="+index);
                if(dispatch is { } value)check(value.StartedMs==position&&value.DurationMs==sample.GetProperty("shineDurations")[index].GetDouble(),"only the last word receives its remaining duration on a new dispatch");
            }
            foreach(var point in sample.GetProperty("samples").EnumerateArray())
            {
                double time=position+sample.GetProperty("durationMs").GetDouble()*point.GetProperty("fraction").GetDouble();
                double actual=WordSweep.Dispatched(layout,selected,position,sample.GetProperty("origin").GetDouble(),time,1,20,sample.GetProperty("chinese").GetBoolean(),text=>text==" "?sample.GetProperty("spaceWidth").GetDouble():prefixWidths[text]);
                check(Math.Abs(actual-point.GetProperty("value").GetDouble())<1e-9,"original remaining-duration sweep starts from its captured mask, position="+position+" elapsed="+(time-position));
            }
            var copy=state.Snapshot();copy.Advance(1500,words.Select(word=>word.Word).ToArray(),-1,true,2);
            check(state.DispatchFor(selected)?.StartedMs==position,"dispatch snapshots do not share a mutable event list");
        }
        }
        var inactive=new WordRunState();inactive.Advance(3000,[new(){StartMs=1000,DurationMs=1000}],-1,true,1,false);
        check(inactive.CurrentWord==-1&&inactive.DispatchFor(0) is null,"a row that never entered cannot dispatch past character effects");
        var progressing=new WordRunState();LyricWord[] progression=[new(){Text="ab ",StartMs=1000,DurationMs=2000,Syllables=[new(){Text="a"},new(){Text="b"}]},new(){Text="world",StartMs=3200,DurationMs=2000}];
        progressing.Advance(1000,progression,-1,true,1);progressing.Advance(3500,progression,-1,true,1);
        check(progressing.DispatchFor(0)?.ShineStarted==true&&progressing.DispatchFor(0)?.StartedMs==1000,"later selection does not cancel a sub-timed word whose Start already ran in an earlier update");
    }
}
