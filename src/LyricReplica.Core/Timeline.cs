using System.Diagnostics;
using System.Globalization;

namespace LyricReplica.Core;

public sealed class PlaybackClock(Func<double>? monotonicMs = null)
{
    private readonly Func<double> _now = monotonicMs ?? (() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);
    private double _anchor;
    private double _position;
    public double DurationMs { get; private set; }
    public bool IsPlaying { get; private set; }
    public long Generation { get; private set; }
    public double PositionMs => Math.Clamp(_position + (IsPlaying ? _now() - _anchor : 0), 0, DurationMs);

    public void Load(double durationMs)
    {
        if (!double.IsFinite(durationMs) || durationMs < 0) throw new ArgumentOutOfRangeException(nameof(durationMs));
        DurationMs = durationMs; _position = 0; _anchor = _now(); IsPlaying = false; Generation++;
    }
    public void Play() { if (IsPlaying) return; _anchor = _now(); IsPlaying = true; }
    public void Pause() { _position = PositionMs; IsPlaying = false; }
    public void Seek(double position)
    {
        if (!double.IsFinite(position)) throw new ArgumentOutOfRangeException(nameof(position));
        _position = Math.Clamp(position, 0, DurationMs); _anchor = _now(); Generation++;
    }
}

public static class Timeline
{
    public static List<DisplayLine> Group(Song song, bool duet, bool rightFirst)
    {
        bool twoSided = song.Lines.Any(x => x.AlignLeft == false);
        var result = new List<DisplayLine>();
        foreach (var line in song.Lines)
        {
            if (line.IsBackground && result.Count > 0)
            {
                var previous = result[^1];
                var backgrounds = previous.Backgrounds.Append(BackgroundText.ForDisplay(line,previous.Main.AlignLeft)).OrderBy(x => x.StartMs).ToArray();
                result[^1] = previous with { Backgrounds = backgrounds, StartMs = Math.Min(previous.StartMs, line.StartMs) };
            }
            else
            {
                bool right = duet && (twoSided ? line.AlignLeft == false : rightFirst);
                result.Add(new DisplayLine(line, [], line.StartMs, right, song.Untimed));
            }
        }
        return result.OrderBy(x => x.StartMs).ToList();
    }

    public static int Anchor(IReadOnlyList<DisplayLine> lines, double time)
    {
        if (lines.Count == 0) return -1;
        if (lines[0].Untimed) return -1;
        int latest = -1;
        for (int i = lines.Count - 1; i >= 0; i--) if (lines[i].StartMs <= time) { latest = i; break; }
        if (latest < 0) return -1;
        while (latest > 0 && lines[latest - 1].EndMs >= time) latest--;
        if(latest==lines.Count-1&&lines[^1].EndMs!=0&&time>lines[^1].EndMs)return lines.Count;
        return latest;
    }

    public static bool Active(DisplayLine line, double time) => !line.Untimed && time > line.StartMs && (line.EndMs == 0 || time <= line.EndMs);

    public static (double Start, double End)? BreathingGap(IReadOnlyList<DisplayLine> lines, double time)
    {
        if (lines.Count == 0) return null;
        if (lines[0].StartMs >= 6000 && time < lines[0].StartMs) return (0, lines[0].StartMs);
        for (int i = 0; i < lines.Count - 1; i++)
        {
            double end = lines[i].EndMs;
            if (end > 0 && lines[i + 1].StartMs - end > 6000 && time > end && time < lines[i + 1].StartMs)
                return (end, lines[i + 1].StartMs);
        }
        return null;
    }

    public static double Fraction(LyricWord word, double time, Func<string, double>? measure = null)
    {
        if (time <= word.StartMs) return 0;
        if (time >= word.EndMs || word.DurationMs <= 0) return 1;
        if (word.Syllables.Count == 0) return Math.Clamp((time - word.StartMs) / word.DurationMs, 0, 1);
        measure ??= text => text.Length;
        double total = measure(string.Concat(word.Syllables.Select(x => x.Text)));
        if (total <= 0) return Math.Clamp((time - word.StartMs) / word.DurationMs, 0, 1);
        string before = "";
        foreach (var syllable in word.Syllables)
        {
            double prefix = measure(before);
            string through = before + syllable.Text;
            if (time < syllable.StartMs) return Math.Clamp(prefix / total, 0, 1);
            if (time <= syllable.EndMs)
            {
                double p = syllable.DurationMs <= 0 ? 1 : Math.Clamp((time - syllable.StartMs) / syllable.DurationMs, 0, 1);
                return Math.Clamp((prefix + (measure(through) - prefix) * p) / total, 0, 1);
            }
            before = through;
        }
        return 1;
    }

    public static bool Emphasis(LyricWord word, bool last, MotionPreset preset)
    {
        string text = word.Text.Trim();
        int n = text.Length;
        bool cjk = n == 1 && IsHan(text[0]);
        if (preset != MotionPreset.Lyricify) return word.DurationMs > 1000 && n is > 0 && n <= ((last || preset == MotionPreset.Android) ? 7 : 6) && !cjk;
        double d = word.DurationMs;
        return n < 8 && ((n > 5 && d / n > 190) || (n is > 3 and <= 5 && d > 1100) || (n == 3 && d > 1225) ||
            (n == 2 && d > 1350) || (d > 1600 && n > 1 && d / n > 180) || (n == 1 && d > (cjk ? 2800 : 1500)));
    }

    public static bool IsCjk(char c) => c is >= '\u0800' and <= '\u9fff' or >= '\uac00' and <= '\ud7ff';
    public static bool IsHan(char c)=>c is >= '\u4e00' and <= '\u9fff';
    public static bool Emphasis(IReadOnlyList<LyricWord> words,int index,bool right,MotionPreset preset)
    {
        var word=words[index];if(word.Text.Length==0)return false;
        var previous=index>0?words[index-1]:null;var next=index+1<words.Count?words[index+1]:null;
        bool currentHan=word.Text.Any(IsHan);
        if(right)
        {
            if(previous is not null&&word.Text[0]!=' '&&!previous.Text.Any(IsHan))return false;
            if(next is not null&&next.Text.Length>0&&next.Text[0]!=' '&&!currentHan)return false;
        }
        else
        {
            if(previous is not null&&previous.Text.Length>0&&previous.Text[^1]!=' '&&!previous.Text.Any(IsHan))return false;
            if(next is not null&&word.Text[^1]!=' '&&!currentHan)return false;
        }
        if(preset==MotionPreset.Lyricify&&next is not null&&word.Text.Trim().Length>6)return false;
        return Emphasis(word,next is null,preset);
    }
    public static string Format(double ms) => $"{(int)Math.Max(ms, 0) / 60000}:{(int)Math.Max(ms, 0) / 1000 % 60:00}";
}

public static class Motion
{
    public static int ScrollDurationMs(AppleMusicSettings settings,bool karaoke)=>settings.InterLineAnimationPreset==InterLineAnimationPresetTypes.iOS?(int)Math.Round(ScrollParameters().Duration*1000):karaoke?500:620;
    public static int BreathingEndExtensionMs(AppleMusicSettings settings,bool karaoke)=>settings.PullingEffect?(int)(ScrollDurationMs(settings,karaoke)*(settings.InterLineAnimationPreset==InterLineAnimationPresetTypes.iOS?1d/31:.03)*settings.PullingDelay*settings.AnimationSpeed):0;
    public static double PullingDelayMs(int distance,double speed=1,double delay=2.5,bool ios=true,bool useBackEase=false)
    {
        if(!double.IsFinite(delay)||delay<0)delay=2.5;
        double result=620*(Math.Max(0,distance)*(ios?1d/31:.03))*delay*speed*(useBackEase&&!ios?1.23:1);
        return Math.Clamp(Math.Truncate(result),0,int.MaxValue);
    }
    public static double Critical(double p, double response, double duration)
    {
        if (p <= 0) return 0;
        if (p >= 1) return 1;
        double x = 2 * Math.PI / Math.Max(response, .001) * p * duration;
        return 1 - (1 + x) * Math.Exp(-x);
    }

    public static (double Duration, double Damping, double Period) ScrollParameters(double? gap = null)
    {
        double u = gap.HasValue ? Math.Clamp((gap.Value - .2) / .55, 0, 1) : 0;
        double zeta = .9 - .12 * u;
        double period = gap.HasValue ? .48 + .27 * u : Math.PI / 5;
        double omega = 2 * Math.PI / period;
        double duration = Math.Log(1 / (Math.Sqrt(1 - zeta * zeta) * .001)) / (zeta * omega);
        return (duration, zeta, period);
    }
    public static double Scroll(double p, double? gap = null)
    {
        if (p <= 0) return 0;
        if (p >= 1) return 1;
        var (duration, zeta, period) = ScrollParameters(gap);
        double omega = 2 * Math.PI / period, wd = omega * Math.Sqrt(1 - zeta * zeta), t = p * duration;
        return 1 - Math.Exp(-zeta * omega * t) * (Math.Cos(wd * t) + zeta * omega / wd * Math.Sin(wd * t));
    }
    public static double LegacyScroll(double progress)
    {
        double x=1-Math.Clamp(progress,0,1),denominator=G(1);
        return 1-(x<=.882?Math.Pow(x/.882,3.08)*.836/denominator:G(x)/denominator);
        static double G(double value)=>1-Math.Pow((1-value)*3/4/.118+.25,3.14)*.16400000000000003;
    }
    public static double LegacyBackScroll(double progress)
    {
        double x=Math.Clamp(progress,0,1);
        return x<=.7898138?52.74*Math.Pow(x,8)-274.3*Math.Pow(x,7)+608.7*Math.Pow(x,6)-746.2*Math.Pow(x,5)+541.1*Math.Pow(x,4)-225.4*Math.Pow(x,3)+44.17*x*x+.1589*x
            :.03139*x*x*x-.0979*x*x+.08382*x+.98269;
    }

    public static (double First, double Second) Mask(double progress, double width, double scale, double feather = 20)
    {
        if (progress <= 0) return (0, 0);
        double d = width + 40 * scale, w = feather * scale;
        if (d <= 0) return (0, 0);
        double a = 0, b = 0;
        if (width > 40 * scale && progress * d < 80 * scale)
        {
            a = (80 * scale - progress * d) / (12 * d);
            b = -(80 * scale - progress * d) / (24 * d);
        }
        if (width > 60 * scale && w > 32 * scale && progress * d < w) b = a - (w - progress * d) / d;
        return (progress + a, Math.Max(progress + a, progress + w / d + b));
    }

    public static (double Scale, double Y, double Glow) Glyph(double timeMs, LyricWord word, int index, int count, MotionPreset preset, double fontSize, double height, bool floating,double floatHeight=2.75,double initialY=0,double initialScale=1)
    {
        if (count <= 0 || index<0 || index>=count || timeMs < word.StartMs) return (initialScale, initialY, 0);
        if(preset==MotionPreset.Lyricify)return LegacyGlyph(timeMs,word,index,count,fontSize,floating,floatHeight,initialY,initialScale);
        double d = Math.Max(word.DurationMs / 1000, .001), t = (timeMs - word.StartMs) / 1000;
        double stagger = Math.Min(.4 * d / count, .4), grow = Math.Round(stagger*(index+1)*1000)/1000, hold = Math.Round(2*d/count*1000)/1000;
        double response = Math.Min(d, 3), multiplier = preset == MotionPreset.Ios264 ? 1.4695433963885915 : 1.5;
        double duration = response * multiplier;
        double clockDuration=TimeSpan.FromSeconds(duration).TotalSeconds;
        double peak = 1 + (Math.Clamp(d, 1, 2) - 1) * .14;
        double lift = floating ? floatHeight * fontSize / 41 : 0;
        double peakLift = preset == MotionPreset.Ios264 ? Math.Max(peak - 1, 0) * height / 4 * Math.Max(1,floatHeight) : 0;
        if (t < grow) return (initialScale, initialY, 0);
        double up = Critical((t - grow) / clockDuration, response, duration);
        double scale = initialScale + (peak - initialScale) * up, y = initialY+(-(lift+peakLift)-initialY)*up;
        if (t > grow + hold)
        {
            double atReturn = Critical(hold / clockDuration, response, duration);
            double down = Critical((t - grow - hold) / clockDuration, response, duration);
            scale = 1 + (initialScale+(peak-initialScale)*atReturn-1)*(1-down);
            if(peakLift>0){double atReturnY=initialY+(-(lift+peakLift)-initialY)*atReturn;y=atReturnY+(-lift-atReturnY)*down;}
        }
        double glow=preset==MotionPreset.Android?.9*(t>grow+hold?Critical(hold/clockDuration,response,duration)*(1-Critical((t-grow-hold)/clockDuration,response,duration)):up):WordGlow(timeMs,word);
        return (scale, y, glow);
    }
    public static (double Scale,double Y,double Glow) StopGlyph((double Scale,double Y,double Glow) from,double elapsedMs,double response,double multiplier,double lift,bool immediate)
    {
        if(immediate)return(1,-lift,0);
        double duration=Math.Max(response*multiplier,.001),progress=Critical(elapsedMs/TimeSpan.FromSeconds(duration).TotalMilliseconds,response,duration);
        return(1+(from.Scale-1)*(1-progress),-lift+(from.Y+lift)*(1-progress),from.Glow*(1-Math.Clamp(elapsedMs/700,0,1)));
    }
    public static double GlyphDelay(LyricWord word,int index,int count,MotionPreset preset)=>preset==MotionPreset.Lyricify?LegacySchedule(word,index,count).GrowAt:Math.Round(Math.Min(.4*(word.DurationMs/1000)/Math.Max(1,count),.4)*(index+1)*1000);
    public static (double Scale,double Y,double Glow) LegacyGlyph(double time,LyricWord word,int index,int count,double fontSize,bool floating,double floatHeight=2.75,double initialY=0,double initialScale=1,double stoppedAtMs=double.PositiveInfinity,bool ios=true)
    {
        if(count<=0||index<0||index>=count||time<word.StartMs)return(initialScale,initialY,0);
        var plan=LegacySchedule(word,index,count);
        double elapsed=time-word.StartMs,scale=initialScale,y=initialY,lift=floating?floatHeight*fontSize/41:0;
        bool started=word.StartMs+plan.GrowAt<stoppedAtMs;
        if(started)
        {
            if(plan.Peak!=1)scale=initialScale+(plan.Peak-initialScale)*Quad((elapsed-plan.GrowAt)/plan.GrowDuration);
            double p=Math.Clamp((elapsed-plan.GrowAt)/Math.Max(plan.Peak==1?1500:650,plan.GrowDuration*1.2),0,1);
            double eased=plan.Peak==1?(ios?Scroll(p):LegacyScroll(p)):p<.5?Math.Pow(2*p,1.2)/2:1-Math.Pow(2*(1-p),1.2)/2;
            y=initialY+(-lift-initialY)*eased;
        }
        // The legacy scale-return worker ignores IsRunning; canceled growth still receives its return clock.
        if(plan.Peak!=1&&elapsed>plan.ReturnAt)
        {
            double value=started?initialScale+(plan.Peak-initialScale)*Quad((plan.ReturnAt-plan.GrowAt)/plan.GrowDuration):initialScale;
            scale=value+(1-value)*Quad((elapsed-plan.ReturnAt)/Math.Max(plan.GrowDuration,650));
        }
        return(scale,y,WordGlow(time,word,stoppedAtMs));
        static double Quad(double progress){double p=Math.Clamp(progress,0,1);return p<.5?2*p*p:1-Math.Pow(-2*p+2,2)/2;}
    }
    private static (double Peak,double GrowDuration,double GrowAt,double ReturnAt) LegacySchedule(LyricWord word,int index,int count)
    {
        double duration=word.DurationMs,step=duration/(count+1),peak=count==2?(word.Text.Contains('i')?1.07:1.083):count==3?1.085:1.1;
        if(step<400&&count>4)peak=1+.1*Math.Pow(Math.Max(0,(step-150)/250),.5);
        else if(step<430&&count==4)peak=1+.1*Math.Pow(Math.Max(0,(step-190)/240),.9);
        else if(step<480&&count==3)peak=1+.1*Math.Pow(Math.Max(0,(step-280)/200),.9);
        else if(step<550&&count<=2)peak=1+.1*(step-380)/170;
        if(step<150)peak=1;
        double growDuration=Math.Min(Math.Max(duration*(count<8?.6:.5),step),1500),growAt=0,previousStep=step;
        int leading=word.Text.StartsWith(' ')?1:0;
        for(int glyph=0;glyph<=index;glyph++)
        {
            double currentStep=step;int prefix=0;
            for(int syllable=0;syllable<word.Syllables.Count;syllable++)
            {
                var item=word.Syllables[syllable];prefix+=item.Text.Length;
                if(glyph+leading>=prefix||item.Text.Length==0)continue;
                double syllableDuration=item.DurationMs;
                if(syllable+1<word.Syllables.Count&&item.EndMs!=word.Syllables[syllable+1].StartMs)syllableDuration=word.Syllables[syllable+1].StartMs-item.StartMs;
                currentStep=Math.Max(80,syllableDuration/item.Text.Length);break;
            }
            growAt+=peak==1?(int)(currentStep/1.12):Math.Min((int)((glyph==0?previousStep:currentStep+previousStep)/3.6),glyph==0?50:500);
            previousStep=currentStep;
        }
        bool cjk=word.Text.Any(Timeline.IsHan);
        double initialWait=duration<4500&&!cjk?Math.Min((int)(step/1.45),400):0;
        double returning=(duration+400)*.85,returnStep=step;
        switch(count)
        {
            case 1:returning*=duration>2500&&cjk?.7:.9;break;
            case 2:returning*=.75;break;
            case 3:returning*=.65;break;
            case 4:if(step<350)returning=duration-700+step;returning*=.6;break;
            case 5:if(step<330)returning=duration;returning*=.6;break;
            default:returning*=.9*.6;returnStep*=.75;break;
        }
        double returnAt=initialWait+(int)returning+(index+1)*(int)Math.Min(returnStep/2.3,70);
        return(peak,growDuration,growAt,returnAt);
    }
    public static double WordGlow(double timeMs,LyricWord word,double stoppedAtMs=double.PositiveInfinity)
    {
        double elapsed=(timeMs-word.StartMs)/1000-.15,d=word.DurationMs/1000;
        if(elapsed<=0)return 0;
        double peak=d>1.7?.43:.43*(d-.5)/1.2,fadeAt=Math.Max(d-.7,0);
        if(stoppedAtMs<=word.StartMs+150||stoppedAtMs<=word.StartMs+150+fadeAt*1000&&elapsed>=fadeAt)return 0;
        double value=peak*Math.Clamp(elapsed/.7,0,1);
        if(elapsed>=fadeAt)value=peak*Math.Clamp(fadeAt/.7,0,1)*(1-Math.Clamp((elapsed-fadeAt)/.7,0,1));
        return Math.Max(0,value);
    }
}
