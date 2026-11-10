using System.Numerics;
using System.Text.Json;
using LyricReplica.Core;

static class PaletteChecks
{
    public static void Run(Action<bool,string> check)
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/palette-original.json")));
        Vector3 Vector(JsonElement value)=>new(value[0].GetSingle(),value[1].GetSingle(),value[2].GetSingle());
        foreach(var item in fixture.RootElement.GetProperty("math").EnumerateArray())
        {
            Vector3 rgb=Vector(item.GetProperty("rgb"));
            check(PaletteMath.IsDark(rgb)==item.GetProperty("isDark").GetBoolean(),"original palette dark classification");
            check(Vector3.Distance(PaletteMath.ToLab(rgb),Vector(item.GetProperty("lab")))<.0001,"original palette LAB conversion");
            check(Vector3.Distance(PaletteMath.Darken(rgb,50),Vector(item.GetProperty("darkened")))<.0001,"original HSV palette darkening");
        }
        foreach(var item in fixture.RootElement.GetProperty("palettes").EnumerateArray())
        {
            var source=item.GetProperty("source").EnumerateArray().ToDictionary(value=>Vector(value),value=>value[3].GetInt32());
            string method=item.GetProperty("method").GetString()!;
            var expected=item.GetProperty("output").EnumerateArray().Select(Vector).ToArray();
            var actual=method switch{"KMeans"=>SourcePalette.KMeans(source),"OctTree"=>SourcePalette.Octree(source),_=>SourcePalette.Auto(source)};
            check(actual.Length==4&&actual.All(v=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z)),"palette always produces four finite colors");
            if(source.Count>4&&method!="OctTree")continue;
            var remaining=expected.ToList();bool equal=true;
            foreach(var color in actual){int index=remaining.FindIndex(value=>Vector3.Distance(value,color)<.001);if(index<0){equal=false;break;}remaining.RemoveAt(index);}
            check(equal&&remaining.Count==0,"original palette output "+method+" / "+source.Count);
        }
        check(SourcePalette.Auto([]).SequenceEqual(new Vector3[4]),"empty palette input has a finite fallback");
    }
}
