using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using LyricReplica.App.Reference;
using LyricReplica.Core;

namespace LyricReplica.App;

internal static class InteractionFeedbackChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var squircle=SquircleGeometry.Create(120,80,15,.6);check(squircle.FillContains(new Point(60,40))&&!squircle.FillContains(new Point(0,0))&&squircle.Bounds.Width<=120.001&&squircle.Bounds.Height<=80.001,"smoothed squircle keeps its center, corner cutout and original bounds");
        check(SquircleGeometry.Create(120,80,15) is RectangleGeometry,"original zero smoothing retains circular rounded corners");
        var hit=new Border();hit.Measure(new Size(100,100));hit.Arrange(new Rect(0,0,100,100));var target=new Grid();bool dragEnabled=true;
        var drag=new ButtonDragFeedback(hit,target,()=>dragEnabled,()=>false,()=>{});drag.Move(new Point(78,22),1000);
        check(target.RenderTransform is TranslateTransform{X:10,Y:-10},"advanced button displacement uses the source 2.8 divisor after entry");dragEnabled=false;drag.Refresh();
        check(!hit.IsHitTestVisible&&target.RenderTransform is TranslateTransform{X:0,Y:0},"disabling advanced button drag returns its transform and hands input back to the real button");
        foreach(ImageButton button in new ImageButton[]{new(),new NextButton()})
        {
            var view=(FrameworkElement)button.Content;var background=(Border)view.FindName("BorderBackground");var scale=(Grid)view.FindName("GridScale");
            Enter(button);Plan(background,UIElement.OpacityProperty,.1,300,"button hover layer");
            Down(button);Plan((ScaleTransform)scale.RenderTransform,ScaleTransform.ScaleXProperty,.88,500,"button press scale");
            Leave(button);Plan(background,UIElement.OpacityProperty,.0001,300,"button leave layer");Plan((ScaleTransform)scale.RenderTransform,ScaleTransform.ScaleYProperty,1,500,"button canceled press returns");
            button.FeedbackMotionEnabled=false;Enter(button);check(background.Opacity==.1&&!background.HasAnimatedProperties,"reduced-motion button still gives immediate hover feedback");Leave(button);check(background.Opacity==.0001,"reduced-motion button hover clears");
        }
        var favorite=new FavoriteButton();var favoriteView=(FrameworkElement)favorite.Content;var favoriteCircle=(Border)favoriteView.FindName("BorderBackgroundCircle");Enter(favorite);
        check(!favoriteCircle.HasAnimatedProperties,"favorite keeps its original empty hover callback instead of receiving the image-button overlay");
        var options=new ViewOptions{Blur=true};options.AppleMusicLyrics.DisableBlurAtLyricsLineMouseOver=true;var row=new LyricRow(new(){Text="line",Words=[new(){Text="line",StartMs=1000,DurationMs=2000}]},false,false,500,1,new FontFamily("Segoe UI"),options);
        var rowView=row.Children.OfType<UserControl>().Single();var rowBackground=(Border)rowView.FindName("BorderBackground");var rowScale=(Grid)rowView.FindName("MainGrid");
        row.Update(0,0,false,1,1000,5000,1);Enter(row);row.Update(0,0,false,1,1000,5000,1);var blur=(BlurEffect)((StackPanel)rowView.FindName("StackPanelLyrics")).Effect;Plan(blur,BlurEffect.RadiusProperty,0,100,"lyric hover blur removal");Leave(row);row.Update(0,0,false,1,1000,5000,1);Plan(blur,BlurEffect.RadiusProperty,4.2,250,"lyric leave blur restoration");
        Enter(row);Plan(rowBackground,UIElement.OpacityProperty,.08,300,"lyric hover layer");Down(row);Plan((ScaleTransform)rowScale.RenderTransform,ScaleTransform.ScaleXProperty,.95,450,"lyric press scale");Leave(row);Plan(rowBackground,UIElement.OpacityProperty,0,300,"lyric leave layer");Plan((ScaleTransform)rowScale.RenderTransform,ScaleTransform.ScaleXProperty,1,450,"lyric canceled press returns");
        row.ContextMenu!.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));Plan(rowBackground,UIElement.OpacityProperty,0,300,"lyric menu close clears highlight");
        row.HoverAllowed=()=>false;rowBackground.BeginAnimation(UIElement.OpacityProperty,null);rowBackground.Opacity=0;Enter(row);check(!rowBackground.HasAnimatedProperties&&rowBackground.Opacity==0,"stationary pointer after scroll cannot create a false lyric hover");
        check(row.Effect is null&&((StackPanel)rowView.FindName("StackPanelLyrics")).Effect is BlurEffect,"line blur affects text content while its hover background stays sharp");
        var credit=(UserControl)Application.LoadComponent(new Uri("/LyricReplica;component/Assets/ReferenceCredit.xaml",UriKind.Relative));var creditBackground=(Border)credit.FindName("BorderBackground");
        _ = new RowFeedback(credit,(Grid)credit.FindName("GridInside"),creditBackground,()=>true,()=>true);Enter(credit);check(creditBackground.Visibility==Visibility.Visible,"credit hover background is no longer left collapsed");Plan(creditBackground,UIElement.OpacityProperty,.08,300,"credit hover layer");
        void Plan(DependencyObject target,DependencyProperty property,double to,int duration,string label)
        {
            var storageType=typeof(AnimationTimeline).Assembly.GetType("System.Windows.Media.Animation.AnimationStorage")!;
            var storage=storageType.GetMethod("GetStorage",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[target,property]);
            var clocks=storageType.GetField("_animationClocks",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(storage!);
            var clock=(AnimationClock)clocks!.GetType().GetProperty("Item")!.GetValue(clocks,[0])!;var plan=(DoubleAnimation)clock.Timeline;
            check(plan.To==to&&plan.Duration.TimeSpan.TotalMilliseconds==duration,"original "+label+" target and duration");
        }
    }
    private static void Enter(UIElement element)=>element.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseEnterEvent});
    private static void Leave(UIElement element)=>element.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseLeaveEvent});
    private static void Down(UIElement element)=>element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent});
}
