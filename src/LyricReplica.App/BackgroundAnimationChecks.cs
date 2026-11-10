using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LyricReplica.Core;

namespace LyricReplica.App;

internal static class BackgroundAnimationChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var buggyOptions=new ViewOptions();buggyOptions.AppleMusicLyrics.BuggyApple=true;
        var buggyLine=new LyricLine{Text="中文",StartMs=1000,EndMs=5000,Words=[new(){Text="中",StartMs=1000,DurationMs=2000},new(){Text="文",StartMs=3000,DurationMs=2000}]};
        var buggy=new LyricRow(buggyLine,false,true,500,1,new FontFamily("Segoe UI"),buggyOptions){BackgroundOverlap=true};
        buggy.Measure(new Size(500,double.PositiveInfinity));buggy.Arrange(new Rect(0,0,500,buggy.DesiredSize.Height));buggy.UpdateLayout();
        buggy.Update(1100,1100,true,0,1000,5000,1,10000);double collapsed=buggy.Margin.Top;
        buggy.Update(1100,1100,true,0,1000,5000,1,10400);
        check(buggy.Margin.Top>collapsed&&((TranslateTransform)((TransformGroup)buggy.RenderTransform).Children[1]).Y==0,"Buggy overlap grows its layout slot while keeping the background movement at zero");
        var chinese=WordEdgeChecks.Find(buggy).OfType<Canvas>().First(canvas=>canvas.Children.OfType<Border>().Any(border=>border.Opacity==1E-5));
        check(chinese.Margin.Top<0&&chinese.Margin.Top+chinese.Margin.Bottom==0,"Chinese abnormal float shifts its strip without changing the reserved height");
        buggy.Update(6000,6000,false,0,1000,5000,2,10500);buggy.Update(6000,6000,false,0,1000,5000,2,12000);
        check(chinese.Margin.Top==0&&chinese.Margin.Bottom==0,"stopped Chinese strip clears its abnormal margin after the original color deadline");
        var overlapMain=new LyricRow(buggyLine,false,false,500,1,new FontFamily("Segoe UI"),buggyOptions);
        var overlapBacking=new LyricRow(buggyLine,false,true,500,1,new FontFamily("Segoe UI"),buggyOptions,above:true)
        {
            BackgroundOverlap=true,
            OpacityTransition=(true,.2)
        };
        overlapMain.AddBackground(overlapBacking,true);
        overlapMain.Measure(new Size(500,double.PositiveInfinity));
        overlapMain.Arrange(new Rect(0,0,500,overlapMain.DesiredSize.Height));
        overlapMain.UpdateLayout();
        double overlapHeight=overlapBacking.NaturalHeight;
        var outside=WordEdgeChecks.Find(overlapBacking).OfType<Grid>().First(grid=>grid.Name=="GridOutside");
        UpdateOverlap(true,10000);
        UpdateOverlap(true,10430);
        // The original 80px bottom plan reaches 77.207060 at 430ms.
        double expectedBottom=-overlapHeight*.965088251810712;
        check(Math.Abs(overlapMain.Margin.Bottom-expectedBottom)<1e-6&&Math.Abs(outside.Margin.Top+overlapBacking.Margin.Top)<1e-6,
            "original overlap contracts the main slot and compensates the backing content position");
        UpdateOverlap(false,10430);
        check(Math.Abs(overlapMain.Margin.Bottom-expectedBottom)<1e-6&&outside.Margin.Top==-3,
            "overlap stop restores backing content geometry while the original main compensation continues");
        UpdateOverlap(false,11720);
        check(overlapMain.Margin.Bottom==0,"main overlap compensation clears at twice the original scroll duration");
        void UpdateOverlap(bool active,double clock)
        {
            double time=active?1100:6000;
            overlapBacking.Update(time,time,active,0,1000,5000,1,clock);
            overlapMain.Update(time,time,active,0,1000,5000,1,clock);
        }
        foreach(var (text,expected) in new[]{("あいう",true),("가나다",true),("กขค",true),("a",false),("中文a",false),("中 文",false)})
        {
            var data=new LyricLine{Text=text,StartMs=1000,EndMs=5000,Words=[new(){Text=text,StartMs=1000,DurationMs=4000}]};
            var row=new LyricRow(data,false,false,500,1,new FontFamily("Segoe UI"),buggyOptions);
            row.Measure(new Size(500,double.PositiveInfinity));row.Arrange(new Rect(0,0,500,row.DesiredSize.Height));row.UpdateLayout();row.Update(1100,1100,true,0,1000,5000,1,10000);
            var strip=WordEdgeChecks.Find(row).OfType<Canvas>().First(canvas=>canvas.Children.OfType<Border>().Any(border=>border.Opacity==1E-5));
            check((strip.Margin.Top<0)==expected,"abnormal floating consumes the original broad character class: "+text);
        }
        foreach(var preset in Enum.GetValues<InterLineAnimationPresetTypes>())
        foreach(int mode in new[]{0,1,2})
        {
            bool above=mode!=1;
            var options=new ViewOptions{Float=false,WordShine=false};var settings=options.AppleMusicLyrics;
            settings.InterLineAnimationPreset=preset;settings.BackgroundVocalsEnterAnimation=mode;settings.BackgroundVocalsEnterDirectionLeft=mode==1;
            var mainData=new LyricLine{Text="main",StartMs=1000,EndMs=5000,Words=[new(){Text="main",StartMs=1000,DurationMs=4000}]};
            var backingData=new LyricLine{Text="echo",StartMs=1500,EndMs=4000,Words=[new(){Text="echo",StartMs=1500,DurationMs=2500}]};
            var main=new LyricRow(mainData,true,false,500,1,new FontFamily("Segoe UI"),options);
            LyricRow Create(double width)=>new(backingData,true,true,width,1,new FontFamily("Segoe UI"),options,above:above){OpacityTransition=(true,.2),BackgroundExitTransition=(true,.7)};
            var backing=Create(main.TextWidth);main.AddBackground(backing,above);main.Measure(new Size(500,double.PositiveInfinity));main.Arrange(new Rect(0,0,500,main.DesiredSize.Height));main.UpdateLayout();
            Update(1100,true,1,10000);var entered=backing.Frame;var move=(TranslateTransform)((TransformGroup)backing.RenderTransform).Children[1];double startY=move.Y;
            Update(1100,true,1,10300);
            check(backing.Frame.RootOpacity>entered.RootOpacity&&backing.Frame.RootScale!=entered.RootScale&&move.Y!=startY,"paused background root opacity, scale and movement continue: "+preset+" mode="+mode);
            check(backing.RenderTransformOrigin.X==(settings.BackgroundVocalsEnterDirectionLeft?0:1)&&Math.Abs(backing.RenderTransformOrigin.Y-(mode==2?.5:.8))<1e-12,"background direction and pivot consume the selected entry mode");
            var host=(FrameworkElement)typeof(LyricRow).GetField("_glyphHost",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(main)!;
            check(!above||host.RenderTransform is TranslateTransform shift&&shift.Y==backing.ParentContentOffsetY,"above-main background applies its own animation clock to the parent text offset");
            var resized=Create(main.TextWidth+20);resized.Measure(new Size(520,double.PositiveInfinity));resized.Arrange(new Rect(0,0,520,resized.DesiredSize.Height));resized.UpdateLayout();resized.RestoreWordRun(backing);
            resized.Update(1100,1100,true,0,1000,5000,1,10350);Update(1100,true,1,10350);
            check(resized.Frame==backing.Frame&&resized.ParentContentOffsetY==backing.ParentContentOffsetY,"background reflow preserves its existing root and parent-offset plans");
            var beforeStop=backing.Frame;Update(6000,false,2,10350);
            check(backing.Frame.RootOpacity==beforeStop.RootOpacity&&backing.Frame.RootScale==beforeStop.RootScale,"background stop captures its current partial root opacity and scale");
            Update(6000,false,2,12350);
            var mask=WordEdgeChecks.Find(backing).OfType<Canvas>().First(canvas=>canvas.Children.OfType<Border>().Any(border=>border.Opacity==1E-5));
            check(backing.Frame.RootOpacity==0&&mask.OpacityMask is null&&Math.Abs(backing.ParentContentOffsetY)<1e-8,"background exit and mask cleanup finish while playback remains paused: "+preset+" mode="+mode);
            void Update(double songTime,bool active,long generation,double clock)
            {backing.Update(songTime,songTime,active,0,1000,5000,generation,clock);main.Update(songTime,songTime,active,0,1000,5000,generation,clock);}
        }
    }
}
