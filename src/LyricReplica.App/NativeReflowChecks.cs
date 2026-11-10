using System.IO;
using System.Text.Json;
using LyricReplica.Core;
using Timeline=LyricReplica.Core.Timeline;

namespace LyricReplica.App;

public sealed partial class MainWindow
{
    private async Task CheckNativeReflowAsync(string folder)
    {
        bool reduced=_options.ReduceMotion;_options.ReduceMotion=false;
        try
        {
            var maskHost=_reference.Get<System.Windows.Controls.Grid>("GridLyricsMaskHost");
            var mask=(System.Windows.Media.LinearGradientBrush)maskHost.OpacityMask;
            int initialAnchor=Timeline.Anchor(_groups,_clock.PositionMs+_options.TimelineOffsetMs);
            double visibleFrom=mask.GradientStops[1].Offset*maskHost.ActualHeight;
            double inkY=_rows[initialAnchor].Main.TranslatePoint(new System.Windows.Point(20*_displayScale,35*_displayScale),maskHost).Y;
            if(visibleFrom>inkY+1)throw new InvalidOperationException($"Portrait mask hides the current lyric: visibleFrom={visibleFrom}, inkY={inkY}, infoHeight={_smallInfo.ActualHeight}.");
            _clock.Pause();_clock.Seek(54000);await Task.Delay(1100);
            if(Timeline.BreathingGap(_groups,_clock.PositionMs+_options.TimelineOffsetMs) is null)throw new InvalidOperationException("The reflow fixture is outside an interlude.");
            Width=1100;Height=713;_rebuildPending=true;await Task.Delay(350);
            double actual=_scroll.VerticalOffset;int anchor=Timeline.Anchor(_groups,_clock.PositionMs+_options.TimelineOffsetMs);
            Follow(anchor,true);double expected=_scrollTo;
            if(Math.Abs(actual-expected)>1)throw new InvalidOperationException($"Paused interlude resize lost its follow target: actual={actual}, expected={expected}.");
            File.WriteAllText(Path.Combine(folder,"native-reflow.json"),JsonSerializer.Serialize(new{passed=true,assemblySha256=App.AssemblySha256,positionMs=_clock.PositionMs,actual,expected,anchor,portraitMaskVisibleFrom=visibleFrom,portraitInkY=inkY,windowShown=true,desktopInputSent=false,scope="Native portrait mask and Loaded/render/resize ordering while paused inside an interlude; not original pixel comparison"},LyricsFile.JsonOptions));
        }
        finally{_options.ReduceMotion=reduced;}
    }
}
