using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using LyricReplica.Core;

namespace LyricReplica.App.Reference;

public sealed class AnimatedTimerReadout : UserControl
{
    public static readonly DependencyProperty TimeProperty=DependencyProperty.Register(nameof(Time),typeof(TimeSpan),typeof(AnimatedTimerReadout),new PropertyMetadata(TimeSpan.Zero,(d,e)=>((AnimatedTimerReadout)d).Refresh()));
    private readonly StackPanel _panel=new(){Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top};
    private readonly TimerPlan _plan=new();
    private readonly TimerGlyph[] _glyphs=Enumerable.Range(0,6).Select(i=>new TimerGlyph(i==3)).ToArray();
    private readonly TextBlock _longTime=new();
    public TimeSpan Time{get=>(TimeSpan)GetValue(TimeProperty);set=>SetValue(TimeProperty,value);}
    public bool Animate{get;set;}=true;
    public string Text=>_longTime.Text;
    public IReadOnlyList<TimerSlot> Slots{get;private set;}=[];
    public AnimatedTimerReadout()
    {
        var root=new Grid();root.Children.Add(_panel);root.Children.Add(_longTime);Content=root;IsHitTestVisible=false;
        foreach(var glyph in _glyphs)_panel.Children.Add(glyph);
        UpdateWidths();Refresh();
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {base.OnPropertyChanged(e);if(e.Property==FontSizeProperty&&_glyphs is not null)UpdateWidths();}
    private void UpdateWidths()
    {
        double width=FontSize/1.67;
        for(int i=0;i<_glyphs.Length;i++){_glyphs[i].Width=width;_glyphs[i].Margin=i==3?new Thickness(-width/4,0,-width/4,0):new Thickness(0);}
    }
    private void Refresh()
    {
        if(_glyphs is null)return;
        // shortcut: three-digit minutes use the static readout; extend the reference's two minute slots for rolling long tracks.
        bool longTrack=Math.Abs(Time.TotalMinutes)>=100;
        _longTime.Visibility=longTrack?Visibility.Visible:Visibility.Collapsed;_panel.Visibility=longTrack?Visibility.Collapsed:Visibility.Visible;
        _longTime.Text=(Time.TotalMilliseconds<0?"-":"")+LyricReplica.Core.Timeline.Format(Math.Abs(Time.TotalMilliseconds));
        System.Windows.Automation.AutomationProperties.SetName(this,_longTime.Text);
        if(longTrack)return;
        Slots=_plan.Update(Time.TotalMilliseconds);
        for(int i=0;i<_glyphs.Length;i++)
        {
            var slot=Slots[i];_glyphs[i].Visibility=slot.Visible?Visibility.Visible:Visibility.Collapsed;
            if(slot.Changed)_glyphs[i].Set(slot.Value,slot.DelayMs,Animate&&Opacity>0);
        }
    }
    private sealed class TimerGlyph : Grid
    {
        private readonly TextBlock _old=new(){HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,RenderTransformOrigin=new Point(.5,.5)},_next=new(){HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,RenderTransformOrigin=new Point(.5,.5)};
        private readonly bool _colon;
        private char _value=' ';
        private Storyboard? _story;
        public TimerGlyph(bool colon)
        {
            _colon=colon;
            foreach(var text in new[]{_old,_next})
            {
                var transforms=new TransformGroup();transforms.Children.Add(new ScaleTransform());transforms.Children.Add(new TranslateTransform());text.RenderTransform=transforms;
                text.Effect=new BlurEffect{Radius=0};Children.Add(text);
            }
            _old.Opacity=0;
            if(colon)foreach(var text in new[]{_old,_next}){text.Inlines.Add(new System.Windows.Documents.Run("0"){Foreground=Brushes.Transparent});text.Inlines.Add(new System.Windows.Documents.Run(":"));text.Inlines.Add(new System.Windows.Documents.Run("0"){Foreground=Brushes.Transparent});}
        }
        public void Set(char value,double delay,bool animate)
        {
            _story?.Remove(this);_story=null;
            if(!_colon){_old.Text=_value.ToString();_next.Text=value.ToString();}
            _value=value;
            if(!animate||_next.ActualHeight<=0){_old.Opacity=0;_next.Opacity=1;return;}
            _old.Opacity=1;_next.Opacity=0;
            double halfHeight=_next.ActualHeight/2,blur=_next.FontSize/2;
            var story=new Storyboard{Duration=TimeSpan.FromMilliseconds(550+delay)};
            var ease=new ExponentialEase{Exponent=4.5,EasingMode=EasingMode.EaseOut};
            Add(_old,"Opacity",Linear(1,0,.45));
            Add(_old,"Effect.(BlurEffect.Radius)",Linear(0,blur,.5));
            Add(_old,"RenderTransform.(TransformGroup.Children)[0].(ScaleTransform.ScaleX)",Linear(1,.5,.5));
            Add(_old,"RenderTransform.(TransformGroup.Children)[0].(ScaleTransform.ScaleY)",Linear(1,.5,.5));
            Add(_old,"RenderTransform.(TransformGroup.Children)[1].(TranslateTransform.Y)",Linear(0,-halfHeight,.5));
            Add(_next,"Opacity",Enter(0,1,.7,ease));
            Add(_next,"Effect.(BlurEffect.Radius)",Enter(blur,0,.9,ease));
            Add(_next,"RenderTransform.(TransformGroup.Children)[0].(ScaleTransform.ScaleX)",Enter(.5,1,.9,ease));
            Add(_next,"RenderTransform.(TransformGroup.Children)[0].(ScaleTransform.ScaleY)",Enter(.5,1,.9,ease));
            Add(_next,"RenderTransform.(TransformGroup.Children)[1].(TranslateTransform.Y)",Enter(halfHeight,0,1,new ElasticEase{Oscillations=1,Springiness=5.5,EasingMode=EasingMode.EaseOut}));
            _story=story;story.Begin(this,HandoffBehavior.SnapshotAndReplace,true);
            DoubleAnimationUsingKeyFrames Linear(double from,double to,double end)
            {
                var animation=new DoubleAnimationUsingKeyFrames{Duration=story.Duration};
                animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from,KeyTime.FromTimeSpan(TimeSpan.Zero)));
                animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from,At(0)));
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(to,At(end)));return animation;
            }
            DoubleAnimationUsingKeyFrames Enter(double from,double to,double end,IEasingFunction easing)
            {
                var animation=Linear(from,from,.15);animation.KeyFrames[2]=new DiscreteDoubleKeyFrame(from,At(.15));animation.KeyFrames.Add(new EasingDoubleKeyFrame(to,At(end)){EasingFunction=easing});return animation;
            }
            KeyTime At(double fraction)=>KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay+550*fraction));
            void Add(DependencyObject target,string path,System.Windows.Media.Animation.Timeline animation){Storyboard.SetTarget(animation,target);Storyboard.SetTargetProperty(animation,new PropertyPath(path));story.Children.Add(animation);}
        }
    }
}
