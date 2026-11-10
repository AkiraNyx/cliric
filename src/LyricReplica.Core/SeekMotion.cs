namespace LyricReplica.Core;

public static class SeekMotion
{
    public static (int TimeMs,double Elastic) Preview(double x,double width,int total)
    {
        if(!double.IsFinite(x)||!double.IsFinite(width))throw new ArgumentOutOfRangeException(nameof(x));
        width=Math.Max(1,width);total=Math.Max(0,total);
        int time=(int)(Math.Clamp(x,0,width)/width*total);
        double beyond=x<0?x:x>width?x-width:0;
        return (time,Math.Sign(beyond)*Math.Pow(Math.Abs(beyond),.76)*1.1);
    }
}
