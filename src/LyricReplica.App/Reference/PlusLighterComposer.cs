using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using LyricReplica.Core;

namespace LyricReplica.App.Reference;

public sealed class PlusLighterComposer(ReferencePageView page) : IDisposable
{
    private sealed record Move(FrameworkElement Element,Panel Parent,int Index,FrameworkElement? Placeholder);
    private readonly List<Move> _moves=[];
    private Grid? _mask,_holder,_overlay;
    private Border? _paint;
    private double _magicOpacity;
    private PlusLighterEffectPresetTypes? _preset;
    public bool Active=>_mask is not null;
    public void Update(bool enabled,PlusLighterEffectPresetTypes preset,Visual background,bool fill)
    {
        if(!enabled){Dispose();return;}
        if(_mask is null)
        {
            try{Build();}catch{Dispose();throw;}
        }
        _paint!.Background=new VisualBrush(background){Stretch=fill?Stretch.Fill:Stretch.Uniform};
        if(_preset!=preset){_preset=preset;_overlay!.Effect=ReferencePlusLighterEffect.ForPreset(preset);}
    }
    private void Build()
    {
        var stage=page.Get<Grid>("MainGrid");_magicOpacity=page.Get<Grid>("GridMagicButton").Opacity;_mask=new Grid{ClipToBounds=true};
        foreach(var column in stage.ColumnDefinitions){var copy=new ColumnDefinition();Bind(copy,ColumnDefinition.WidthProperty,column,nameof(ColumnDefinition.Width));_mask.ColumnDefinitions.Add(copy);}
        var anchor=new Border{Background=Brushes.White,Opacity=1E-5};Grid.SetColumnSpan(anchor,2);_mask.Children.Add(anchor);
        Reparent(page.Get<Grid>("GridLyrics"),_mask,false);
        _mask.Children.Add(LargeInfo());_mask.Children.Add(SmallInfo(false));_mask.Children.Add(SmallInfo(true));
        var magic=page.Get<Grid>("GridMagicButton");_magicOpacity=magic.Opacity;magic.Opacity=.01;_mask.Children.Add(Magic());
        _holder=new Grid{ClipToBounds=true,Opacity=.01};Grid.SetColumnSpan(_holder,2);_holder.Children.Add(_mask);
        _paint=new Border{OpacityMask=new VisualBrush(_mask)};
        _overlay=new Grid{IsHitTestVisible=false};Grid.SetColumnSpan(_overlay,2);Panel.SetZIndex(_overlay,1);_overlay.Children.Add(_paint);
        int index=Math.Max(1,stage.Children.IndexOf(page.Get<Viewbox>("ViewboxSongInfo")));index=Math.Min(index,stage.Children.Count);
        stage.Children.Insert(index,_overlay);stage.Children.Insert(index+1,_holder);
    }
    private FrameworkElement LargeInfo()
    {
        var source=page.Get<Viewbox>("ViewboxSongInfo");var view=new Viewbox();CopyPosition(source,view);Bind(view,FrameworkElement.MarginProperty,source,nameof(source.Margin));Bind(view,UIElement.VisibilityProperty,source,nameof(source.Visibility));
        var stack=new StackPanel{Width=450,VerticalAlignment=VerticalAlignment.Center};Bind(stack,FrameworkElement.MarginProperty,page.Get<StackPanel>("StackPanelSongInfo"),"Margin");
        stack.Children.Add(new Grid{Width=330,Height=330,Margin=new Thickness(0,0,0,25)});
        Reparent(page.Get<Grid>("GridTrackInfo"),stack,true);Reparent(page.Get<FrameworkElement>("AppleMusicPlayerStateBar"),stack,true);
        var controls=page.Get<Grid>("GridHorizontalControls");var clone=new Grid{RenderTransform=controls.RenderTransform,RenderTransformOrigin=controls.RenderTransformOrigin};
        Bind(clone,FrameworkElement.MarginProperty,controls,nameof(controls.Margin));Bind(clone,UIElement.VisibilityProperty,controls,nameof(controls.Visibility));
        foreach(var column in controls.ColumnDefinitions)clone.ColumnDefinitions.Add(new ColumnDefinition{Width=column.Width});
        Reparent(page.Get<FrameworkElement>("ImageClickableShuffleButton"),clone,true);Reparent(page.Get<FrameworkElement>("ImageClickableRepeatButton"),clone,true);
        stack.Children.Add(clone);view.Child=stack;return view;
    }
    private FrameworkElement SmallInfo(bool overlay)
    {
        var source=page.Get<Viewbox>(overlay?"ViewboxSongInfoSmallOverlay":"ViewboxSongInfoSmall");var view=new Viewbox{VerticalAlignment=VerticalAlignment.Top};CopyPosition(source,view);
        Bind(view,FrameworkElement.MarginProperty,source,nameof(source.Margin));Bind(view,UIElement.VisibilityProperty,source,nameof(source.Visibility));
        var original=page.Get<Grid>(overlay?"GridSongInfoSmallOverlay":"GridSongInfoSmall");var grid=new Grid{Margin=new Thickness(35)};
        Bind(grid,FrameworkElement.WidthProperty,original,nameof(original.Width));
        var column=new ColumnDefinition();Bind(column,ColumnDefinition.WidthProperty,page.Get<ColumnDefinition>("ViewboxSongInfoSmallColumnDefinition1"),"Width");grid.ColumnDefinitions.Add(column);grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=new GridLength(128)});
        grid.RowDefinitions.Add(new());grid.RowDefinitions.Add(new());
        if(overlay)
        {
            Bind(grid,FrameworkElement.HeightProperty,original,nameof(original.ActualHeight));
            Reparent(page.Get<Grid>("GridSongInfoSmallArtistHost"),grid,true);
            Reparent(page.Get<FrameworkElement>("FavoriteButtonVerticalMode"),grid,true);Reparent(page.Get<Grid>("GridThreeDotMenuVerticalMode"),grid,true);
        }
        else
        {
            var blank=new Border{HorizontalAlignment=HorizontalAlignment.Left};var artwork=page.Get<ArtworkImage>("WebImageAlbumSmall");
            Bind(blank,FrameworkElement.WidthProperty,artwork,nameof(artwork.Width));Bind(blank,FrameworkElement.HeightProperty,artwork,nameof(artwork.Height));Grid.SetRowSpan(blank,2);grid.Children.Add(blank);
            Reparent(page.Get<Grid>("GridSongInfoSmallTitleHost"),grid,true);
        }
        view.Child=grid;return view;
    }
    private FrameworkElement Magic()
    {
        var source=page.Get<Grid>("GridMagicButton");var grid=new Grid{IsHitTestVisible=false};CopyPosition(source,grid);
        foreach(var (property,name) in new[]{(FrameworkElement.HeightProperty,"Height"),(FrameworkElement.MarginProperty,"Margin"),(FrameworkElement.HorizontalAlignmentProperty,"HorizontalAlignment"),(FrameworkElement.VerticalAlignmentProperty,"VerticalAlignment"),(UIElement.VisibilityProperty,"Visibility")})Bind(grid,property,source,name);
        grid.Children.Add(new Border{Background=Brushes.White,Opacity=1E-5});var inside=new Grid();Bind(inside,UIElement.RenderTransformProperty,page.Get<Grid>("GridMagicButtonInside"),"RenderTransform");grid.Children.Add(inside);
        inside.Children.Add(new Border{Background=Brushes.White,Opacity=1E-5,CornerRadius=new CornerRadius(30)});
        var original=page.Get<Border>("BorderMagicButtonInside");var bar=new Border{Background=Brushes.White};
        foreach(var (property,name) in new[]{(FrameworkElement.WidthProperty,"ActualWidth"),(FrameworkElement.HeightProperty,"ActualHeight"),(FrameworkElement.MarginProperty,"Margin"),(UIElement.OpacityProperty,"Opacity"),(Border.CornerRadiusProperty,"CornerRadius")})Bind(bar,property,original,name);
        inside.Children.Add(bar);var symbolHost=new Border();
        foreach(var (property,name) in new[]{(FrameworkElement.WidthProperty,"ActualWidth"),(FrameworkElement.HeightProperty,"ActualHeight"),(FrameworkElement.MarginProperty,"Margin")})Bind(symbolHost,property,original,name);
        var sourceGlyph=page.Get<Viewbox>("ViewboxMagicButtonInside");var glyphBox=new Viewbox();Bind(glyphBox,UIElement.OpacityProperty,sourceGlyph,"Opacity");Bind(glyphBox,FrameworkElement.MarginProperty,sourceGlyph,"Margin");glyphBox.Child=TextProxy(page.Get<TextBlock>("SymbolMagicButton"));symbolHost.Child=glyphBox;inside.Children.Add(symbolHost);
        var hints=new Grid();Bind(hints,FrameworkElement.MarginProperty,original,"Margin");for(int i=1;i<=4;i++)hints.Children.Add(TextProxy(page.Get<TextBlock>("TextBlockMagicButtonTip"+i)));inside.Children.Add(hints);
        return grid;
    }
    private static TextBlock TextProxy(TextBlock source)
    {
        var text=new TextBlock{Foreground=Brushes.White,Margin=source.Margin,HorizontalAlignment=source.HorizontalAlignment,VerticalAlignment=source.VerticalAlignment,TextAlignment=source.TextAlignment};
        foreach(var (property,name) in new[]{(TextBlock.TextProperty,"Text"),(TextBlock.FontFamilyProperty,"FontFamily"),(TextBlock.FontSizeProperty,"FontSize"),(TextBlock.FontWeightProperty,"FontWeight"),(UIElement.VisibilityProperty,"Visibility"),(UIElement.OpacityProperty,"Opacity")})Bind(text,property,source,name);
        return text;
    }
    private void Reparent(FrameworkElement element,Panel destination,bool leaveSpace)
    {
        if(element.Parent is not Panel parent)throw new InvalidOperationException("Blend input has no panel parent: "+element.Name);
        int index=parent.Children.IndexOf(element);parent.Children.Remove(element);FrameworkElement? placeholder=null;
        if(leaveSpace)
        {
            placeholder=new Grid{Background=Brushes.Transparent,IsHitTestVisible=false,Margin=element.Margin,HorizontalAlignment=element.HorizontalAlignment,VerticalAlignment=element.VerticalAlignment,MinWidth=element.MinWidth,MinHeight=element.MinHeight,MaxWidth=element.MaxWidth,MaxHeight=element.MaxHeight};
            Bind(placeholder,FrameworkElement.WidthProperty,element,"ActualWidth");Bind(placeholder,FrameworkElement.HeightProperty,element,"ActualHeight");Bind(placeholder,UIElement.VisibilityProperty,element,"Visibility");CopyPosition(element,placeholder);parent.Children.Insert(index,placeholder);
        }
        _moves.Add(new(element,parent,index,placeholder));destination.Children.Add(element);
    }
    private static void Bind(DependencyObject target,DependencyProperty property,object source,string path)=>BindingOperations.SetBinding(target,property,new Binding(path){Source=source});
    private static void CopyPosition(UIElement source,UIElement target)
    {Grid.SetColumn(target,Grid.GetColumn(source));Grid.SetRow(target,Grid.GetRow(source));Grid.SetColumnSpan(target,Grid.GetColumnSpan(source));Grid.SetRowSpan(target,Grid.GetRowSpan(source));Panel.SetZIndex(target,Panel.GetZIndex(source));}
    public void Dispose()
    {
        var stage=page.Get<Grid>("MainGrid");
        if(_holder is not null)_holder.Opacity=1;
        if(_overlay is not null){_overlay.Effect=null;_overlay.Children.Clear();stage.Children.Remove(_overlay);}
        if(_holder is not null)stage.Children.Remove(_holder);
        for(int i=_moves.Count-1;i>=0;i--)
        {
            var move=_moves[i];if(move.Element.Parent is Panel current)current.Children.Remove(move.Element);
            if(move.Placeholder?.Parent is Panel placeholderParent)placeholderParent.Children.Remove(move.Placeholder);
            move.Parent.Children.Insert(Math.Min(move.Index,move.Parent.Children.Count),move.Element);
        }
        _moves.Clear();
        if(_holder is not null)_holder.Children.Clear();
        if(_mask is not null)page.Get<Grid>("GridMagicButton").Opacity=_magicOpacity;
        _mask=null;_holder=null;_overlay=null;_paint=null;_preset=null;
    }
}
