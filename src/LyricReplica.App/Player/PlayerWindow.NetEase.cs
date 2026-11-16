using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LyricReplica.App;

public sealed partial class PlayerWindow
{
    private void OpenNetEaseSearch(object sender, RoutedEventArgs e) => ShowNetEaseSearch(null);

    private void OnSidebarSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        e.Handled = true;
        ShowNetEaseSearch(SidebarSearch.Text.Trim());
    }

    private void ShowNetEaseSearch(string? query)
    {
        using var lifetime = new CancellationTokenSource();
        var dialog = new SettingsWindow(this, "网易云音乐", 620, 540);
        var layout = new DockPanel();
        var searchBar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var keyword = new TextBox { MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(keyword, "歌名或艺人");
        var search = new Button { Content = "搜索", MinWidth = 70, Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(search, Dock.Right);
        searchBar.Children.Add(search);
        searchBar.Children.Add(keyword);
        DockPanel.SetDock(searchBar, Dock.Top);
        layout.Children.Add(searchBar);

        var status = new TextBlock { Text = "输入歌名或艺人", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        var signIn = new Button { Content = "扫码登录客户端会话", MinHeight = 30, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(signIn, Dock.Top);
        layout.Children.Add(signIn);
        var results = new ListBox { Height = 280 };
        signIn.Click += (_, _) =>
        {
            if (!ShowNetEaseLogin(dialog))
                return;
            results.Items.Clear();
            status.Text = "客户端会话已登录，可以搜索歌曲。";
        };
        dialog.Activated += (_, _) => signIn.Content = NetEaseMusic.HasClientSession ? "客户端会话已保存 · 重新扫码" : "扫码登录客户端会话";
        DockPanel.SetDock(status, Dock.Top);
        layout.Children.Add(status);
        var play = new Button { Content = "播放所选歌曲", IsEnabled = false, MinHeight = 30, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(play, Dock.Bottom);
        layout.Children.Add(play);
        layout.Children.Add(results);
        dialog.SettingsContent = layout;

        bool busy = false;
        dialog.Closed += (_, _) => lifetime.Cancel();
        keyword.Text = query ?? "";
        dialog.Loaded += async (_, _) =>
        {
            keyword.Focus();
            if (keyword.Text.Length > 0)
                await SearchAsync();
        };
        search.Click += async (_, _) => await SearchAsync();
        keyword.PreviewKeyDown += async (_, args) =>
        {
            if (args.Key != Key.Enter)
                return;
            args.Handled = true;
            await SearchAsync();
        };
        results.SelectionChanged += (_, _) => play.IsEnabled = !busy && results.SelectedItem is ListBoxItem { Tag: NetEaseTrack { Playable: true } };
        results.MouseDoubleClick += async (_, _) => await PlayAsync();
        play.Click += async (_, _) => await PlayAsync();
        dialog.ShowDialog();

        async Task SearchAsync()
        {
            if (busy || string.IsNullOrWhiteSpace(keyword.Text))
                return;
            await RunAsync("正在搜索…", async () =>
            {
                bool client = NetEaseMusic.HasClientSession;
                var tracks = client
                    ? await NetEaseMusic.SearchClientAsync(keyword.Text.Trim(), lifetime.Token)
                    : await NetEaseMusic.SearchAsync(keyword.Text.Trim(), lifetime.Token);
                results.Items.Clear();
                foreach (var track in tracks)
                {
                    string display = $"{track.Title}  ·  {track.Artist}\n{track.Availability}";
                    results.Items.Add(new ListBoxItem { Content = display, Tag = track, Padding = new Thickness(8, 5, 8, 5) });
                }
                status.Text = tracks.Length == 0 ? "没有找到歌曲。" : client
                    ? $"客户端会话找到 {tracks.Length} 首，播放时检查所选版本的完整音源。"
                    : $"开放平台找到 {tracks.Length} 首。可扫码切换到客户端会话。";
            });
        }

        async Task PlayAsync()
        {
            if (busy || results.SelectedItem is not ListBoxItem { Tag: NetEaseTrack { Playable: true } track })
                return;
            await RunAsync("正在读取歌词并启动播放…", async () =>
            {
                await _lyrics.PlayNetEaseAsync(track, lifetime.Token);
                RefreshPlayer();
                dialog.Close();
            });
        }

        async Task RunAsync(string message, Func<Task> action)
        {
            busy = true;
            search.IsEnabled = play.IsEnabled = signIn.IsEnabled = false;
            status.Text = message;
            try { await action(); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception exception) { status.Text = exception.Message; }
            finally
            {
                busy = false;
                search.IsEnabled = signIn.IsEnabled = true;
                play.IsEnabled = results.SelectedItem is ListBoxItem { Tag: NetEaseTrack { Playable: true } };
            }
        }
    }

    private static bool ShowNetEaseLogin(Window owner)
    {
        using var lifetime = new CancellationTokenSource();
        var token = lifetime.Token;
        var dialog = new SettingsWindow(owner, "网易云扫码登录", 410, 430);
        var panel = new StackPanel();
        var image = new Image { Width = 200, Height = 200, Margin = new Thickness(0, 0, 0, 12) };
        System.Windows.Automation.AutomationProperties.SetName(image, "网易云登录二维码");
        var message = new TextBlock { Text = "正在获取二维码…", TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center };
        var retry = new Button { Content = "刷新二维码", IsEnabled = false, Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(image);
        panel.Children.Add(message);
        panel.Children.Add(retry);
        dialog.SettingsContent = panel;
        dialog.Closed += (_, _) => lifetime.Cancel();
        dialog.Loaded += async (_, _) => await RunLoginAsync();
        retry.Click += async (_, _) => await RunLoginAsync();
        return dialog.ShowDialog() == true;

        async Task RunLoginAsync()
        {
            retry.IsEnabled = false;
            image.Source = null;
            message.Text = "正在获取二维码…";
            try
            {
                var qr = await NetEaseMusic.CreateClientQrAsync(token);
                image.Source = Artwork.Decode(qr.Image);
                message.Text = "请用手机网易云音乐扫码并确认登录。";
                while (true)
                {
                    await Task.Delay(1800, token);
                    int state = await NetEaseMusic.PollClientQrAsync(qr.Key, token);
                    if (state == 803)
                    {
                        dialog.DialogResult = true;
                        return;
                    }
                    if (state == 800)
                    {
                        message.Text = "二维码已过期，请刷新。";
                        return;
                    }
                    if (state is not (801 or 802))
                    {
                        message.Text = "网易云暂未接受登录，请刷新后重试。";
                        return;
                    }
                    if (state == 802)
                        message.Text = "已扫码，请在手机上确认。";
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception exception) { message.Text = exception.Message; }
            finally { retry.IsEnabled = !token.IsCancellationRequested; }
        }
    }
}
