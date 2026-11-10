extern alias emoji;
using EmojiTextBlock=emoji::Emoji.Wpf.TextBlock;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LyricReplica.Core;

namespace LyricReplica.App;

internal static class LineAnimationChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var options=new ViewOptions{Float=false,WordShine=false};
        var line=new LyricLine{Text="hello world",Translation="translation",StartMs=1000,EndMs=5000,Words=[new(){Text="hello ",StartMs=1000,DurationMs=2000},new(){Text="world",StartMs=3000,DurationMs=2000}]};
        LyricRow Create(double width=500){var result=new LyricRow(line,false,false,width,1,new FontFamily("Segoe UI"),options);result.Measure(new Size(width,double.PositiveInfinity));result.Arrange(new Rect(0,0,width,result.DesiredSize.Height));result.UpdateLayout();return result;}
        var row=Create();row.Update(1100,1100,true,0,1000,5000,1,10000);var entered=row.Frame;
        row.Update(1100,1100,true,0,1000,5000,1,10075);
        check(row.Frame.PlayedAlpha>entered.PlayedAlpha&&row.Frame.SecondaryAlpha>entered.SecondaryAlpha&&row.Frame.ContentScale>entered.ContentScale,"paused current row continues its color, translation and content-scale entry");
        row.Update(1100,1100,true,0,1000,5000,1,10100);var beforeStop=row.Frame;var mask=Mask(row);byte beforeAlpha=((LinearGradientBrush)mask.OpacityMask).GradientStops[0].Color.A;
        var split=Create(160);split.RestoreWordRun(row);split.Update(1100,1100,true,0,1000,5000,1,10100);var frozenMask=Mask(split);byte frozenAlpha=((LinearGradientBrush)frozenMask.OpacityMask).GradientStops[0].Color.A;
        split.Update(6000,6000,false,1,1000,5000,2,10200);
        check(((LinearGradientBrush)frozenMask.OpacityMask).GradientStops[0].Color.A==frozenAlpha,"a split strip stops from its own frozen color instead of the continuing parent color");
        split.Update(6000,6000,false,1,1000,5000,2,10375);
        check(((LinearGradientBrush)frozenMask.OpacityMask).GradientStops[0].Color.A<frozenAlpha,"a split strip receives a new exit color clock after reflow");
        row.Update(6000,6000,false,1,1000,5000,2,10100);
        check(Math.Abs(row.Frame.ContentScale-beforeStop.ContentScale)<1e-12&&Math.Abs(row.Frame.SecondaryAlpha-beforeStop.SecondaryAlpha)<1e-12,"a seek-triggered stop captures the current partial scale and translation without a jump");
        check(mask.OpacityMask is LinearGradientBrush stopped&&stopped.GradientStops[0].Color.A==beforeAlpha&&row.RunningWord==0,"stopping word dispatch preserves the current mask and quantized color snapshot across a seek generation");
        row.Update(6000,6000,false,1,1000,5000,2,10275);
        check(mask.OpacityMask is LinearGradientBrush fading&&fading.GradientStops[0].Color.A<beforeAlpha&&row.Frame.SecondaryAlpha<beforeStop.SecondaryAlpha,"paused stopped row continues fading on its exit clock");
        row.Update(6000,6000,false,1,1000,5000,2,11000);
        check(mask.OpacityMask is null&&Math.Abs(mask.Opacity-47d/255)<1e-12,"the real-time stop deadline removes the word mask while playback remains paused");
        row.Update(1100,1100,true,0,1000,5000,3,11100);row.Update(1100,1100,true,0,1000,5000,3,11175);
        var resized=Create(520);resized.RestoreWordRun(row);resized.Update(1100,1100,true,0,1000,5000,3,11200);row.Update(1100,1100,true,0,1000,5000,3,11200);
        check(resized.Frame==row.Frame&&((LinearGradientBrush)Mask(resized).OpacityMask).GradientStops.Select(stop=>stop.Color).SequenceEqual(((LinearGradientBrush)Mask(row).OpacityMask).GradientStops.Select(stop=>stop.Color)),"same-count reflow preserves the existing line and mask color clocks");
        var beforeSeek=row.Frame;row.Update(1200,1200,true,0,1000,5000,4,11200);
        check(row.Frame==beforeSeek,"seeking inside the same playing row does not restart its line-state animations");
        var plainOptions=new ViewOptions{AppleMusicLyrics=new(){TranslationLinePreset=TranslationLinePresetTypes.iOS}};
        var plain=new LyricRow(new(){Text="plain",Translation="translation"},false,false,500,1,new FontFamily("Segoe UI"),plainOptions,karaoke:false);
        plain.Update(1100,1100,true,0,1000,5000,1,10000);plain.Update(1100,1100,true,0,1000,5000,1,10075);
        check(Math.Abs(plain.Frame.PlayedAlpha-.65)<1e-12&&plain.Frame.SecondaryAlpha<1,"ordinary text and translation keep their original linear entry during pause");
        var delayed=Create();delayed.SwitchDelayMs=300;delayed.Update(1100,1100,true,0,1000,5000,1,20000);delayed.Update(1100,1100,true,0,1000,5000,1,20200);
        check(delayed.Frame.PlayedAlpha>47d/255&&delayed.Frame.ContentScale==1,"switch delay holds content scale while lyric colors enter immediately");
        delayed.Update(1100,1100,true,0,1000,5000,1,20400);
        check(delayed.Frame.ContentScale>1,"content scale starts after the page switch delay even while paused");
        var romajiOptions=new ViewOptions{RomajiOption=RomajiOptions.RomajiOnly};
        var romajiLine=new LyricLine{Text="fixture",Translation="translation ©",Romanization="romaji"};
        var romajiRow=new LyricRow(romajiLine,false,false,500,1,new FontFamily("Segoe UI"),romajiOptions);
        check(WordEdgeChecks.Find(romajiRow).OfType<TextBlock>().Any(text=>text.Text=="romaji"&&text.Visibility==Visibility.Visible)&&!WordEdgeChecks.Find(romajiRow).OfType<TextBlock>().Any(text=>text.Text==romajiLine.Translation&&text.Visibility==Visibility.Visible),"romaji-only mode suppresses translation while preserving pronunciation");
        romajiOptions.RomajiOption=RomajiOptions.TranslationFirst;
        var emojiRow=new LyricRow(romajiLine,false,false,500,1,new FontFamily("Segoe UI"),romajiOptions);
        check(WordEdgeChecks.Find(emojiRow).OfType<EmojiTextBlock>().Any(text=>text.Text==romajiLine.Translation&&text.EmojiInlines.Any()),"colorful emoji setting selects the original emoji-aware translation renderer");
        var symbolRow=new LyricRow(new(){Text="fixture",Translation="translation ☆"},false,false,500,1,new FontFamily("Segoe UI"),romajiOptions);
        check(!WordEdgeChecks.Find(symbolRow).OfType<EmojiTextBlock>().Any(),"a plain star stays in the normal translation renderer as in the original matcher");
        romajiOptions.EnableColorfulEmoji=false;var monoRow=new LyricRow(romajiLine,false,false,500,1,new FontFamily("Segoe UI"),romajiOptions);
        check(!WordEdgeChecks.Find(monoRow).OfType<EmojiTextBlock>().Any(),"disabling colorful emoji retains the ordinary translation renderer");
        static Canvas Mask(LyricRow row)=>WordEdgeChecks.Find(row).OfType<Canvas>().First(canvas=>canvas.Children.OfType<Border>().Any(border=>border.Opacity==1E-5));
    }
}
