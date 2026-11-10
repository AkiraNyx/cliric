using System.Numerics;

namespace LyricReplica.Core;

public static class SourcePalette
{
    public static Vector3[] Auto(Dictionary<Vector3,int> source,int count=4)
    {var kmeans=KMeans(source,count,true);var octree=Octree(source,count);return PaletteMath.Variance(kmeans)>PaletteMath.Variance(octree)?kmeans:octree;}
    private static Dictionary<Vector3,int> DarkColors(Dictionary<Vector3,int> source)
    {
        var selected=source.Where(pair=>PaletteMath.IsDark(pair.Key)).ToDictionary();
        if(selected.Count>0)return selected;
        selected=new(source);foreach(var pair in source){var darker=PaletteMath.Darken(pair.Key,50);selected[darker]=selected.GetValueOrDefault(darker)+pair.Value;}return selected;
    }
    public static Vector3[] KMeans(Dictionary<Vector3,int> source,int count=4,bool plusPlus=false)
    {
        if(count<1||count>256)throw new ArgumentOutOfRangeException(nameof(count));if(source.Count==0)return new Vector3[count];
        var colors=DarkColors(source);int clusters=Math.Min(count,colors.Count);var keys=colors.Keys.ToArray();Vector3[] centers;
        if(plusPlus&&source.Count>1)
        {
            var selected=new List<Vector3>{keys[Random.Shared.Next(keys.Length)]};
            while(selected.Count<clusters)
            {
                float sum=0;var cumulative=new float[keys.Length];
                for(int i=0;i<keys.Length;i++){float distance=selected.Min(center=>Vector3.Distance(center,keys[i]));cumulative[i]=sum+=distance*distance;}
                float target=(float)Random.Shared.NextDouble()*sum;int index=Array.FindIndex(cumulative,value=>value>=target);selected.Add(keys[Math.Max(0,index)]);
            }
            centers=selected.ToArray();
        }
        else centers=keys.OrderBy(_=>Guid.NewGuid()).Take(clusters).ToArray();
        var groups=Enumerable.Range(0,clusters).Select(_=>new Dictionary<Vector3,int>()).ToArray();bool changed=true;
        for(int iteration=0;changed&&iteration<250;iteration++)
        {
            changed=false;foreach(var group in groups)group.Clear();
            foreach(var pair in colors)
            {
                float nearest=float.MaxValue;int index=0;
                for(int i=0;i<centers.Length;i++){float distance=Vector3.Distance(pair.Key,centers[i]);if(distance<nearest){nearest=distance;index=i;}}
                groups[index][pair.Key]=pair.Value;
            }
            for(int i=0;i<clusters;i++)
            {
                if(groups[i].Count==0)continue;float r=0,g=0,b=0,weight=0;
                foreach(var pair in groups[i]){r+=pair.Key.X*pair.Value;g+=pair.Key.Y*pair.Value;b+=pair.Key.Z*pair.Value;weight+=pair.Value;}
                var center=new Vector3(r/weight,g/weight,b/weight);if(center!=centers[i]){centers[i]=center;changed=true;}
            }
        }
        return Enumerable.Range(0,count).Select(i=>centers[i%centers.Length]).ToArray();
    }
    public static Vector3[] Octree(Dictionary<Vector3,int> source,int count=4)
    {
        if(count<1||count>256)throw new ArgumentOutOfRangeException(nameof(count));if(source.Count==0)return new Vector3[count];
        var levels=Enumerable.Range(0,8).Select(_=>new List<Node>()).ToArray();var root=new Node();
        foreach(var pair in DarkColors(source))root.Add(pair.Key,pair.Value,0,levels);
        int remaining=levels[7].Count-count;
        for(int depth=6;depth>=0&&remaining>0;depth--)
        {
            bool skipped=false;
            foreach(var node in levels[depth].Where(node=>node.ChildrenCount-1<=remaining).OrderBy(node=>node.ChildrenCount))
            {
                if(node.ChildrenCount<=remaining){remaining-=node.ChildrenCount-1;node.Collapse();if(remaining<=0)break;}
                else skipped=true;
            }
            levels[depth+1].Clear();if(skipped)break;
        }
        var palette=new Dictionary<Vector3,int>();root.Collect(palette);var colors=palette.OrderByDescending(pair=>pair.Value).Take(count).Select(pair=>pair.Key).ToArray();
        return colors.Length==0?new Vector3[count]:Enumerable.Range(0,count).Select(i=>colors[i%colors.Length]).ToArray();
    }
    private sealed class Node
    {
        private Node?[] _children=new Node?[8];private Vector3 _color;private int _weight;
        public int ChildrenCount=>_children.Count(child=>child is not null);
        public void Add(Vector3 color,int weight,int depth,List<Node>[] levels)
        {
            if(depth==8){_color=color;_weight+=weight;return;}
            int mask=128>>depth,index=(((byte)color.X&mask)!=0?4:0)|(((byte)color.Y&mask)!=0?2:0)|(((byte)color.Z&mask)!=0?1:0);
            if(_children[index] is null){_children[index]=new Node();levels[depth].Add(_children[index]!);}_children[index]!.Add(color,weight,depth+1,levels);
        }
        public void Collect(Dictionary<Vector3,int> colors)
        {if(ChildrenCount==0){colors[_color]=_weight;return;}foreach(var child in _children)child?.Collect(colors);}
        public void Collapse()
        {
            var children=_children.OfType<Node>().ToArray();
            if(children.Length==1)_color=new((int)children[0]._color.X,(int)children[0]._color.Y,(int)children[0]._color.Z);
            else{int total=children.Sum(child=>child._weight);_color=new(children.Sum(child=>child._color.X*child._weight)/total,children.Sum(child=>child._color.Y*child._weight)/total,children.Sum(child=>child._color.Z*child._weight)/total);}
            _weight=children.Sum(child=>child._weight);_children=new Node?[8];
        }
    }
}
