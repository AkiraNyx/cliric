namespace LyricReplica.Core;

public static class BackgroundText
{
    public static LyricLine ForDisplay(LyricLine source,bool? alignLeft)
    {
        bool brackets=source.Text.Length>1&&((source.Text[0]=='('&&source.Text[^1]==')')||(source.Text[0]=='（'&&source.Text[^1]=='）'));
        var words=source.Words.Select((word,index)=>new LyricWord
        {
            Text=Clean(word.Text,index==0,index==source.Words.Count-1),StartMs=word.StartMs,DurationMs=word.DurationMs,
            Syllables=word.Syllables.Select(s=>new Syllable{Text=s.Text,StartMs=s.StartMs,DurationMs=s.DurationMs}).ToList()
        }).ToList();
        return new LyricLine{Id=source.Id,Text=brackets?source.Text[1..^1].Trim():source.Text,Translation=source.Translation,Romanization=source.Romanization,StartMs=source.StartMs,EndMs=source.EndMs,Background=true,AlignLeft=alignLeft,Words=words};
        string Clean(string text,bool first,bool last)
        {
            if(!brackets)return text;
            if(first&&text.Length>0&&text[0]==source.Text[0])text=text[1..];
            if(last&&text.Length>0&&text[^1]==source.Text[^1])text=text[..^1];
            return text;
        }
    }
}
