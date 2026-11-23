using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace Cliric.App;

internal static class RuntimeChecks
{
    internal static async Task RunAsync(string folder)
    {
        var power = new SleepPrevention();
        bool acquired = false, released = false;
        try
        {
            acquired = power.Update(true, false, WindowState.Normal) && power.IsHeld;
            if (!acquired)
                throw new InvalidOperationException("Windows rejected the real sleep prevention request.");
        }
        finally { released = power.Release() && !power.IsHeld; }
        if (!released)
            throw new InvalidOperationException("The real sleep prevention request was not released.");
        MediaSnapshot? observed = null;
        string? mediaError = null;
        using (var media = new SystemMediaConnection(Dispatcher.CurrentDispatcher))
        {
            media.Changed += snapshot => observed = snapshot;
            media.Error += message => mediaError = message;
            var connect = media.ConnectAsync();
            if (await Task.WhenAny(connect, Task.Delay(5000)) != connect)
                mediaError = "System media read exceeded 5 seconds.";
            else
                await connect;
        }
        IsolationGpuChecks.Run(folder);
        File.WriteAllText(Path.Combine(folder, "runtime.json"), JsonSerializer.Serialize(new
        {
            assemblySha256 = App.AssemblySha256,
            powerRequestAcquired = acquired,
            powerRequestReleased = released,
            systemMediaApiRead = true,
            mediaObserved = observed is not null,
            media = observed is null ? null : new
            {
                observed.Title,
                observed.Artist,
                observed.PositionMs,
                observed.DurationMs,
                observed.Playing,
                observed.ShuffleActive,
                observed.RepeatMode,
                observed.CanShuffle,
                observed.CanRepeat
            },
            mediaError,
            gpuReport = "isolation-gpu.json",
            desktopInputSent = false,
            windowShown = false,
            transportCommandsSent = false,
            scope = "Real Windows power API, read-only system media connection, Direct2D WARP/hardware and D3DImage bridge; independent visible host and full-page comparison require native acceptance."
        }, Cliric.Core.LyricsFile.JsonOptions));
    }
}
