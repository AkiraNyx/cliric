using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Cliric.Core;

namespace Cliric.App;

internal sealed record NetEaseTrack(string Id, long OriginalId, string Title, string Artist, string Album, double DurationMs, bool Playable, bool ClientSession = false, string CoverUrl = "", string ClientRights = "")
{
    public string Availability => ClientRights == "preview" ? "仅试听" : !Playable ? "当前授权不可播" :
        !ClientSession || ClientRights == "full" ? "可播放" : "权限未确认";
}

internal static partial class NetEaseMusic
{
    private const string PipeName = "ncm-mpv";
    private static readonly string[] AudioOnlyOptions = ["video=no", "audio-display=no", "force-window=no", "terminal=no", "vo=null", "media-controls=no"];
    private static readonly string ToolRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../tools/ncm-cli-0.1.7"));
    private static readonly HttpClient CoverClient = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });

    public static async Task<NetEaseTrack[]> SearchAsync(string keyword, CancellationToken cancellationToken)
    {
        var data = GetData(await RunAsync(cancellationToken, "search", "song", "--keyword", keyword, "--limit", "30"));
        return data.GetProperty("records").EnumerateArray().Select(track => new NetEaseTrack(
            track.GetProperty("id").GetString()!, track.GetProperty("originalId").GetInt64(),
            ReadText(track, "name"), string.Join(" / ", track.GetProperty("artists").EnumerateArray().Select(artist => ReadText(artist, "name"))),
            track.TryGetProperty("album", out var album) ? ReadText(album, "name") : "",
            track.GetProperty("duration").GetDouble(), IsTrue(track, "visible") && IsTrue(track, "playFlag"),
            CoverUrl: ReadText(track, "coverImgUrl"))).ToArray();
    }

    public static async Task<BitmapSource?> LoadCoverAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 || !uri.Host.EndsWith(".music.126.net", StringComparison.OrdinalIgnoreCase))
            return null;

        var secureUri = new UriBuilder(uri) { Scheme = "https", Port = -1 }.Uri;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var response = await CoverClient.GetAsync(secureUri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > Artwork.MaximumMediaBytes)
            return null;

        using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var image = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int count;
        while ((count = await body.ReadAsync(buffer, timeout.Token)) != 0)
        {
            if (image.Length + count > Artwork.MaximumMediaBytes)
                return null;
            image.Write(buffer, 0, count);
        }
        return Artwork.Decode(image.ToArray());
    }

    public static async Task<Song> LoadLyricsAsync(NetEaseTrack track, CancellationToken cancellationToken, bool useLyricifyParser = false)
    {
        if (track.ClientSession)
            return await LoadClientLyricsAsync(track, cancellationToken, useLyricifyParser);
        var response = await RunAsync(cancellationToken, "song", "lyric", "--songId", track.Id);
        var data = response.TryGetProperty("data", out var payload) ? payload : default;
        string lyric = ReadText(data, "lyric");
        Func<string, Song> parseLrc = useLyricifyParser ? LyricifyParser.ParseLrc : LyricsFile.ParseLrc;
        var song = parseLrc(string.IsNullOrWhiteSpace(lyric) ? ReadText(data, "txtLyric") : lyric);
        ApplySecondary(ReadText(data, "transLyric"), (line, value) => line.Translation = value);
        ApplySecondary(ReadText(data, "romalrc"), (line, value) => line.Romanization = value);
        song.Id = $"netease-{track.OriginalId}";
        song.Title = track.Title;
        song.Artist = track.Artist;
        song.Album = track.Album;
        song.DurationMs = track.DurationMs;
        song.Validate();
        return song;

        void ApplySecondary(string text, Action<LyricLine, string> assign)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            var secondary = parseLrc(text);
            if (secondary.Untimed)
                return;
            var lines = secondary.Lines.ToLookup(line => line.StartMs);
            foreach (var line in song.Lines)
                if (lines[line.StartMs].FirstOrDefault() is { } match)
                    assign(line, match.Text);
        }
    }

    public static async Task<string> StartAsync(NetEaseTrack track, double volume, string quality, CancellationToken cancellationToken, double positionMs, bool playing)
    {
        if (track.ClientSession)
            return await StartClientPlaybackAsync(track, volume, quality, cancellationToken, positionMs, playing);
        if (!track.Playable)
            throw new InvalidOperationException("网易云当前授权不允许播放这首歌。");
        string mpvDirectory = Path.Combine(ToolRoot, "mpv");
        if (!File.Exists(Path.Combine(mpvDirectory, "mpv.exe")))
            throw new InvalidOperationException("缺少项目内的 mpv 播放组件。");
        // The CLI starts mpv itself; use mpv's Windows portable config for this project copy.
        string configDirectory = Path.Combine(mpvDirectory, "portable_config");
        Directory.CreateDirectory(configDirectory);
        string configPath = Path.Combine(configDirectory, "mpv.conf");
        if (!File.Exists(configPath))
            File.WriteAllLines(configPath, AudioOnlyOptions);
        var response = await RunAsync(cancellationToken, "play", "--song", "--encrypted-id", track.Id,
            "--original-id", track.OriginalId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        // The Windows daemon can close its reply stream after starting mpv; verify through mpv itself.
        if (response.ValueKind != JsonValueKind.Undefined)
            RequireSuccess(response);
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startup.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            while (!startup.IsCancellationRequested)
            {
                try
                {
                    if (await ReadPlaybackAsync(startup.Token) is { DurationMs: > 0, Ended: false })
                    {
                        await SetAsync("volume", volume * 100, startup.Token);
                        return "";
                    }
                }
                catch (IOException) when (!startup.IsCancellationRequested) { }
                await Task.Delay(200, startup.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("网易云音频未能启动。");
    }

    public static async Task StopAsync(CancellationToken cancellationToken)
    {
        if (await StopClientPlaybackAsync())
            return;
        RequireSuccess(await RunAsync(cancellationToken, "stop"));
    }

    public static async Task<(double PositionMs, double DurationMs, bool Playing, bool Ended)> ReadPlaybackAsync(CancellationToken cancellationToken)
    {
        var values = await SendAsync(cancellationToken, ["get_property", "time-pos"], ["get_property", "duration"],
            ["get_property", "pause"], ["get_property", "idle-active"], ["get_property", "paused-for-cache"]);
        double position = values[0].ValueKind == JsonValueKind.Number ? values[0].GetDouble() * 1000 : 0;
        double duration = values[1].ValueKind == JsonValueKind.Number ? values[1].GetDouble() * 1000 : 0;
        bool ended = values[3].ValueKind == JsonValueKind.True;
        bool playing = duration > 0 && !ended && values[2].ValueKind == JsonValueKind.False && values[4].ValueKind != JsonValueKind.True;
        return (position, duration, playing, ended);
    }

    public static async Task SetAsync(string property, object value, CancellationToken cancellationToken)
    {
        var results = await SendAsync(cancellationToken, ["set_property", property, value]);
        if (results[0].ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException("网易云播放组件没有接受操作。");
    }

    internal static async Task<int> GetPlaybackProcessIdAsync(CancellationToken cancellationToken)
    {
        var results = await SendAsync(cancellationToken, ["get_property", "pid"]);
        return results[0].GetInt32();
    }

    private static async Task<JsonElement[]> SendAsync(CancellationToken cancellationToken, params object[][] commands)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        var token = timeout.Token;
        using var pipe = new NamedPipeClientStream(".", _clientPipe ?? PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(token);
            using var reader = new StreamReader(pipe);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            var results = new JsonElement[commands.Length];
            for (int i = 0; i < commands.Length; i++)
                await writer.WriteLineAsync(JsonSerializer.Serialize(new { command = commands[i], request_id = i + 1 }).AsMemory(), token);
            for (int remaining = commands.Length; remaining > 0;)
            {
                string line = await reader.ReadLineAsync(token) ?? throw new IOException("网易云播放组件已断开。");
                using var document = JsonDocument.Parse(line);
                var response = document.RootElement;
                if (!response.TryGetProperty("request_id", out var id) || !id.TryGetInt32(out int index) || index < 1 || index > commands.Length)
                    continue;
                if (ReadText(response, "error") == "success")
                    results[index - 1] = response.TryGetProperty("data", out var data) ? data.Clone() : JsonSerializer.SerializeToElement(true);
                remaining--;
            }
            return results;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException("网易云播放组件未响应。");
        }
    }

    private static async Task<JsonElement> RunAsync(CancellationToken cancellationToken, params string[] arguments)
    {
        string cli = Path.Combine(ToolRoot, "node_modules", "@music163", "ncm-cli", "dist", "index.js");
        if (!File.Exists(cli))
            throw new InvalidOperationException("找不到项目内的网易云官方工具。");
        string profile = Path.Combine(ToolRoot, "profile");
        var start = new ProcessStartInfo("node.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = ToolRoot
        };
        start.Environment["USERPROFILE"] = profile;
        start.Environment["APPDATA"] = Path.Combine(profile, "Roaming");
        start.Environment["LOCALAPPDATA"] = Path.Combine(profile, "Local");
        start.Environment["PATH"] = Path.Combine(ToolRoot, "mpv") + Path.PathSeparator + start.Environment["PATH"];
        foreach (string key in new[] { "NETEASE_APP_ID", "NETEASE_PRIVATE_KEY", "NETEASE_APP_SECRET" })
            start.Environment.Remove(key);
        start.ArgumentList.Add(cli);
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        start.ArgumentList.Add("--output");
        start.ArgumentList.Add("json");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动网易云官方工具。");
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            string output = await stdout;
            string errors = await stderr;
            if (process.ExitCode == 0 && arguments[0] == "play" && string.IsNullOrWhiteSpace(output))
                return default;
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                if (errors.Contains("未设置", StringComparison.Ordinal) || output.Contains("未登录", StringComparison.Ordinal))
                    throw new InvalidOperationException("请先配置网易云凭证并扫码登录。");
                throw new InvalidOperationException("网易云官方工具未返回有效结果，请检查登录状态。");
            }
            using var document = JsonDocument.Parse(output);
            if (ReadText(document.RootElement, "message").Contains("未登录", StringComparison.Ordinal))
                throw new InvalidOperationException("请先配置网易云凭证并扫码登录。");
            return document.RootElement.Clone();
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("网易云请求超时。");
        }
    }

    private static JsonElement GetData(JsonElement response)
    {
        if (response.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number && code.GetInt32() == 200 &&
            response.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            return data;
        throw new InvalidOperationException("网易云未授权此请求，或接口暂不可用。");
    }

    private static void RequireSuccess(JsonElement response)
    {
        if (!IsTrue(response, "success"))
            throw new InvalidOperationException("网易云未接受播放请求，请检查授权和曲目状态。");
    }

    private static bool IsTrue(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var field) && field.ValueKind == JsonValueKind.True;

    private static string ReadText(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString()! : "";
}
