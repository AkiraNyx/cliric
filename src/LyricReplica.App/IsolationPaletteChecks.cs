using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LyricReplica.App.Reference;
using LyricReplica.Core;

namespace LyricReplica.App;

internal static class IsolationPaletteChecks
{
    public static void Run(Action<bool,string> check)
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("palette-original.json")!;
        using var fixture=JsonDocument.Parse(stream);
        foreach(var item in fixture.RootElement.GetProperty("images").EnumerateArray())
        {
            int[][] colors=item.GetProperty("colors").EnumerateArray().Select(color=>color.EnumerateArray().Select(value=>value.GetInt32()).ToArray()).ToArray();
            var pixels=new byte[40*40*4];
            for(int y=0;y<40;y++)for(int x=0;x<40;x++){int i=(y*40+x)*4;int[] color=colors[Math.Min(colors.Length-1,x/10)];pixels[i]=(byte)color[2];pixels[i+1]=(byte)color[1];pixels[i+2]=(byte)color[0];pixels[i+3]=255;}
            var image=BitmapSource.Create(40,40,96,96,PixelFormats.Bgra32,null,pixels,160);
            var palette=IsolationPalette.Extract(Artwork.Encode(image),(IsolationPalettes)item.GetProperty("mode").GetInt32());
            var expected=item.GetProperty("output").EnumerateArray().Select(color=>new Vector3(color[0].GetSingle(),color[1].GetSingle(),color[2].GetSingle())).ToList();
            bool equal=palette.Length==4;
            foreach(var color in palette){int index=expected.FindIndex(value=>Vector3.Distance(color,value)<.00001);if(index<0){equal=false;break;}expected.RemoveAt(index);}
            check(equal&&expected.Count==0,"original bitmap palette path "+item.GetProperty("name")+" / "+item.GetProperty("mode"));
        }
        var transparent=BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new byte[4],4);
        check(IsolationPalette.Extract(Artwork.Encode(transparent),IsolationPalettes.MedianCut1).SequenceEqual(new Vector3[4]),"transparent cover cannot produce an empty-palette failure");
    }
}
