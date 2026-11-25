using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cliric.Core;

namespace Cliric.App;

internal static partial class NetEaseMusic
{
    private static readonly string ClientRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../tools/netease-client"));
    private static string SessionFile => Path.Combine(ClientRoot, "session.dpapi");
    private static Process? _clientMpv;
    private static string? _clientPipe;

    public static bool HasClientSession => File.Exists(SessionFile);
    internal sealed record ClientAccount(string Name, string AvatarUrl);
    internal sealed record ClientPlaylist(long Id, string Name, string CoverUrl);

    public static void ForgetClientSession() => File.Delete(SessionFile);

    public static async Task<string[]> ReadClientQualityAsync(NetEaseTrack track, CancellationToken token)
    {
        var response = await RunClientAsync(new { operation = "quality", id = track.OriginalId, cookie = ReadClientCookie() }, token);
        return response.GetProperty("levels").EnumerateArray().Select(level => level.GetString() ?? "").ToArray();
    }

    public static async Task<(string Key, byte[] Image)> CreateClientQrAsync(CancellationToken token)
    {
        var response = await RunClientAsync(new { operation = "qr" }, token);
        const string prefix = "data:image/png;base64,";
        string image = ReadText(response, "image");
        string key = ReadText(response, "key");
        if (key.Length == 0 || image.Length > 500_000 || !image.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("网易云没有返回有效的登录二维码。");
        return (key, Convert.FromBase64String(image[prefix.Length..]));
    }

    public static async Task<int> PollClientQrAsync(string key, CancellationToken token)
    {
        var response = await RunClientAsync(new { operation = "poll", key }, token);
        if (!response.TryGetProperty("code", out var code) || !code.TryGetInt32(out int state))
            throw new InvalidOperationException("网易云未返回有效的扫码状态。");
        if (state != 803)
            return state;
        string cookie = string.Join("; ", ReadText(response, "cookie").Split(';', StringSplitOptions.TrimEntries)
            .Where(value => value.StartsWith("MUSIC_U=", StringComparison.Ordinal) || value.StartsWith("__csrf=", StringComparison.Ordinal)));
        if (!cookie.Split(';', StringSplitOptions.TrimEntries).Any(value => value.StartsWith("MUSIC_U=", StringComparison.Ordinal) && value.Length > 8))
            throw new InvalidOperationException("扫码已确认，但没有收到可用的网易云会话。");
        token.ThrowIfCancellationRequested();
        byte[] encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(cookie), null, DataProtectionScope.CurrentUser);
        string temporary = SessionFile + ".tmp";
        File.WriteAllBytes(temporary, encrypted);
        File.Move(temporary, SessionFile, overwrite: true);
        return state;
    }

    public static async Task<NetEaseTrack[]> SearchClientAsync(string keyword, CancellationToken token)
    {
        var response = await RunClientAsync(new { operation = "search", keyword, cookie = ReadClientCookie() }, token);
        return ParseClientTracks(response);
    }

    public static async Task<ClientAccount?> ReadClientAccountAsync(CancellationToken token)
    {
        var response = await RunClientAsync(new { operation = "account", cookie = ReadClientCookie() }, token);
        return IsTrue(response, "signedIn") ? new ClientAccount(ReadText(response, "name"), ReadText(response, "avatarUrl")) : null;
    }

    public static async Task<ClientPlaylist[]> ReadClientRecommendationsAsync(CancellationToken token)
    {
        var response = await RunClientAsync(new { operation = "recommendations", cookie = ReadClientCookie() }, token);
        return response.GetProperty("playlists").EnumerateArray()
            .Where(item => item.TryGetProperty("id", out var id) && id.TryGetInt64(out long value) && value > 0)
            .Select(item => new ClientPlaylist(item.GetProperty("id").GetInt64(), ReadText(item, "name"), ReadText(item, "coverUrl"))).ToArray();
    }

    public static async Task<(NetEaseTrack[] Tracks, int? NextOffset, long[]? TrackIds)> ReadClientLibraryAsync(
        string operation, long id, int offset, CancellationToken token, long[]? trackIds = null)
    {
        var response = await RunClientAsync(new { operation, id, offset, trackIds, cookie = ReadClientCookie() }, token);
        int? nextOffset = response.TryGetProperty("nextOffset", out var next) && next.ValueKind == JsonValueKind.Number &&
            next.TryGetInt32(out int value) && value > offset ? value : null;
        var ids = response.TryGetProperty("trackIds", out var index) && index.ValueKind == JsonValueKind.Array
            ? index.EnumerateArray().Select(value => value.TryGetInt64(out long trackId) ? trackId : 0).ToArray()
            : null;
        return (ParseClientTracks(response), nextOffset, ids);
    }

    internal static NetEaseTrack[] ParseClientTracks(JsonElement response)
    {
        if (!response.TryGetProperty("songs", out var songs) || songs.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("网易云未返回歌曲列表，请检查登录状态。");
        response.TryGetProperty("rights", out var rights);
        var tracks = new List<NetEaseTrack>();
        foreach (var item in songs.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var id) || !id.TryGetInt64(out long originalId) || originalId <= 0 ||
                !item.TryGetProperty("dt", out var duration) || !duration.TryGetDouble(out double durationMs) || durationMs is <= 0 or > 86_400_000)
                continue;
            string artist = item.TryGetProperty("ar", out var artists) && artists.ValueKind == JsonValueKind.Array
                ? string.Join(" / ", artists.EnumerateArray().Select(value => ReadText(value, "name"))) : "";
            item.TryGetProperty("al", out var album);
            string access = ReadText(rights, originalId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            tracks.Add(new NetEaseTrack("", originalId, ReadText(item, "name"), artist,
                ReadText(album, "name"), durationMs, access is not ("preview" or "unavailable"),
                ClientSession: true, CoverUrl: ReadText(album, "picUrl"), ClientRights: access));
        }
        return tracks.ToArray();
    }

    private static async Task<Song> LoadClientLyricsAsync(NetEaseTrack track, CancellationToken token, bool useCliricExtendedParser)
    {
        var response = await RunClientAsync(new { operation = "lyrics", id = track.OriginalId }, token);
        return ParseClientLyrics(track, response, useCliricExtendedParser);
    }

    internal static Song ParseClientLyrics(NetEaseTrack track, JsonElement response, bool useCliricExtendedParser = false)
    {
        Func<string, Song> parseLrc = useCliricExtendedParser ? CliricExtendedParser.ParseLrc : LyricsFile.ParseLrc;
        Func<string, List<LyricLine>> parseYrc = useCliricExtendedParser ? CliricExtendedParser.ParseYrc : YrcLyrics.Parse;
        var song = parseLrc(LyricText("lrc"));
        var timed = parseYrc(LyricText("yrc"));
        if (timed.Count > 0)
        {
            song.Lines = timed;
            song.Untimed = false;
        }
        AttachText("ytlrc", "tlyric", (line, value) => line.Translation = value);
        AttachText("yromalrc", "romalrc", (line, value) => line.Romanization = value);
        song.Id = $"netease-{track.OriginalId}";
        song.Title = track.Title;
        song.Artist = track.Artist;
        song.Album = track.Album;
        song.DurationMs = track.DurationMs;
        song.Validate();
        return song;

        string LyricText(string key) => response.TryGetProperty(key, out var item) ? ReadText(item, "lyric") : "";

        void AttachText(string wordKey, string lineKey, Action<LyricLine, string> assign)
        {
            var wordLines = parseYrc(LyricText(wordKey)).ToLookup(line => line.StartMs);
            var lineLyrics = parseLrc(LyricText(lineKey));
            var lineTimes = lineLyrics.Lines.ToLookup(line => line.StartMs);
            foreach (var line in song.Lines)
            {
                var match = wordLines[line.StartMs].FirstOrDefault()
                    ?? (lineLyrics.Untimed ? null : lineTimes[line.StartMs].FirstOrDefault());
                if (match is not null)
                    assign(line, match.Text);
            }
        }
    }

    private static async Task<string> StartClientPlaybackAsync(NetEaseTrack track, double volume, string level, CancellationToken token, double positionMs, bool playing)
    {
        var audio = await RunClientAsync(new { operation = "audio", id = track.OriginalId, level, cookie = ReadClientCookie() }, token);
        if (IsTrue(audio, "preview"))
            throw new InvalidOperationException("网易云只返回了试听片段，当前账号没有取得完整音源。");
        if (!audio.TryGetProperty("code", out var code) || !code.TryGetInt32(out int status) || status != 200 ||
            !Uri.TryCreate(ReadText(audio, "url"), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0)
            throw new InvalidOperationException("当前客户端会话没有取得这首歌的完整音源，请确认网易云账号的播放权限。");
        string executable = Path.Combine(ToolRoot, "mpv", "mpv.exe");
        if (!File.Exists(executable))
            throw new InvalidOperationException("缺少项目内的 mpv 播放组件。");
        token.ThrowIfCancellationRequested();
        _clientPipe = "cliric-netease-" + Guid.NewGuid().ToString("N");
        try
        {
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
            foreach (string option in AudioOnlyOptions)
                start.ArgumentList.Add("--" + option);
            foreach (string argument in new[] { "--no-config", "--idle=yes", "--pause=yes",
                "--input-ipc-server=" + _clientPipe, "--", uri.AbsoluteUri })
                start.ArgumentList.Add(argument);
            _clientMpv = Process.Start(start) ?? throw new InvalidOperationException("无法启动网易云播放组件。");
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(token);
            startup.CancelAfter(TimeSpan.FromSeconds(15));
            while (!_clientMpv.HasExited)
            {
                try
                {
                    var state = await ReadPlaybackAsync(startup.Token);
                    if (state.DurationMs > 0 && !state.Ended)
                    {
                        if (state.DurationMs + Math.Max(2500, track.DurationMs * .02) < track.DurationMs)
                            throw new InvalidOperationException("网易云返回的音频短于该曲目的完整时长。");
                        await SetAsync("volume", volume * 100, startup.Token);
                        if (positionMs > 0)
                            await SetAsync("time-pos", Math.Clamp(positionMs, 0, state.DurationMs) / 1000, startup.Token);
                        if (playing)
                            await SetAsync("pause", false, startup.Token);
                        return ReadText(audio, "level");
                    }
                }
                catch (IOException) when (!startup.IsCancellationRequested) { }
                await Task.Delay(200, startup.Token);
            }
            throw new InvalidOperationException("网易云音频未能启动。");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new InvalidOperationException("网易云音源加载超时。");
        }
    }

    private static async Task<bool> StopClientPlaybackAsync()
    {
        if (_clientPipe is null)
            return false;
        var process = _clientMpv;
        if (process is not null && !process.HasExited)
        {
            try { await SendAsync(CancellationToken.None, ["quit"]); }
            catch (IOException) { }
            if (!process.HasExited)
                process.Kill();
            await process.WaitForExitAsync();
        }
        process?.Dispose();
        _clientMpv = null;
        _clientPipe = null;
        return true;
    }

    private static string ReadClientCookie()
    {
        if (!HasClientSession)
            throw new InvalidOperationException("请先扫码登录网易云客户端会话。");
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(SessionFile), null, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("网易云登录记录无法读取，请重新扫码。");
        }
    }

    private static async Task<JsonElement> RunClientAsync(object request, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(Path.Combine(ClientRoot, "node_modules", "@neteasecloudmusicapienhanced", "api")))
            throw new InvalidOperationException("找不到项目内的网易云客户端接口组件。");
        var start = new ProcessStartInfo("node.exe")
        {
            WorkingDirectory = ClientRoot, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8
        };
        start.Environment["TEMP"] = ClientRoot;
        start.Environment["TMP"] = ClientRoot;
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Media", "NetEaseClient.cjs"));
        start.ArgumentList.Add(ClientRoot);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动网易云客户端接口组件。");
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errors = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(request).AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            await errors;
            const string prefix = "CLIRIC_RESULT ";
            string? result = (await output).Split('\n').LastOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
            if (process.ExitCode != 0 || result is null)
                throw new InvalidOperationException("网易云客户端接口请求失败，请稍后重试或重新扫码。");
            using var document = JsonDocument.Parse(result[prefix.Length..]);
            return document.RootElement.Clone();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("网易云客户端接口请求超时。");
        }
        finally
        {
            if (!process.HasExited)
                process.Kill();
        }
    }
}
