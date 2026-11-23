using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Cliric.App;

public sealed partial class LibraryView
{
    private sealed record SearchRow(NetEaseTrack Track, ImageSource? Artwork, string Duration);
    private sealed record PlaylistTile(NetEaseMusic.ClientPlaylist Playlist, ImageSource? Artwork);
    private CancellationTokenSource? _loginLifetime;
    private CancellationTokenSource? _playlistLoad;
    private bool _searchBusy;
    private int _accountGeneration;
    private bool _checkingAccount;
    private bool _accountExpanded;
    private bool _loadingPlaylists;
    private bool? _accountSignedIn;
    private string? _accountName, _accountDetail;
    private string? _libraryOperation;
    private long _libraryPlaylistId;
    private long[]? _libraryTrackIds;
    private int? _nextLibraryOffset;
    private int? _suspendedPlaylistOffset;
    private bool UseClientSession => NetEaseMusic.HasClientSession && _accountSignedIn != false;

    private static string? QualityName(string level) => level switch
    {
        "standard" => "标准",
        "higher" => "较高",
        "exhigh" => "极高",
        "lossless" => "无损",
        "hires" => "Hi-Res 无损",
        "jyeffect" => "高清臻音",
        "sky" => "沉浸环绕声",
        "jymaster" => "超清母带",
        "vivid" => "臻音全景声",
        _ => null
    };

    private async void OpenQualityMenu(object sender, RoutedEventArgs e)
    {
        if (_lyrics.PlaybackLoading || _lyrics.ClientTrack is not { } track)
            return;
        var menu = new ContextMenu { PlacementTarget = NetEaseQualityButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
        var status = new MenuItem { Header = "正在检查此账号可用的音质…", IsEnabled = false };
        menu.Items.Add(status);
        NetEaseQualityButton.ContextMenu = menu;
        OpenShellMenu(menu);
        try
        {
            var levels = await NetEaseMusic.ReadClientQualityAsync(track, _lifetime.Token);
            if (!menu.IsOpen || !ReferenceEquals(_lyrics.ClientTrack, track))
                return;
            foreach (string level in levels)
            {
                if (QualityName(level) is not { } label)
                    continue;
                var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = _lyrics.ActualNetEaseQuality == level };
                item.Click += async (_, _) =>
                {
                    try { await _lyrics.SwitchNetEaseQualityAsync(track, level, _lifetime.Token); }
                    catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
                    catch (Exception error) { ShowNotice(error.Message); }
                };
                menu.Items.Add(item);
            }
            if (menu.Items.Count == 1)
                status.Header = "平台未返回此曲可用的完整音质";
            else
                menu.Items.Remove(status);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { status.Header = error.Message; }
    }

    private async void OnSidebarSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        e.Handled = true;
        SearchQuery.Text = SidebarSearch.Text.Trim();
        ShowPage(SearchPage, animate: false);
        await SearchAsync();
    }

    private async void OnSearchQueryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        e.Handled = true;
        await SearchAsync();
    }

    internal async Task SearchAsync()
    {
        string keyword = SearchQuery.Text.Trim();
        if (_searchBusy || keyword.Length == 0)
            return;
        ResetNetEaseResults("搜索");
        await RunSearchAsync("正在搜索…", async token =>
        {
            bool client = UseClientSession;
            var tracks = client
                ? await NetEaseMusic.SearchClientAsync(keyword, token)
                : await NetEaseMusic.SearchAsync(keyword, token);
            ShowNetEaseTracks(tracks);
            SearchStatus.Text = tracks.Length == 0 ? $"没有找到与“{keyword}”相关的歌曲。" : client
                ? $"找到 {tracks.Length} 首"
                : $"找到 {tracks.Length} 首 · 开放平台结果，扫码登录后可切换到客户端会话";
        });
    }

    private void ResetNetEaseResults(string heading)
    {
        _searchTitle = heading;
        if (_page == SearchPage)
            UpdatePageTitle();
        SearchResults.ItemsSource = null;
        SearchStatus.Text = "";
        SearchPage.ScrollToHome();
    }

    private async void OpenNetEaseLibrary(object sender, RoutedEventArgs e)
    {
        if (_searchBusy || sender is not Button { Tag: string operation, Content: string heading })
            return;
        await OpenPlaylistAsync(heading, operation, 0, null);
    }

    private async void OpenLikedPlaylist(object sender, RoutedEventArgs e)
    {
        if (!_searchBusy)
            await OpenPlaylistAsync("我喜欢的音乐", "liked", 0, null);
    }

    private async Task LoadHomePlaylistsAsync()
    {
        if (_selfTest || _accountSignedIn != true || _loadingPlaylists || RecommendedPlaylists.ItemsSource is not null)
            return;
        _loadingPlaylists = true;
        int generation = _accountGeneration;
        RecommendedStatus.Text = "正在加载推荐歌单…";
        RecommendedStatus.Visibility = Visibility.Visible;
        try
        {
            var playlists = await NetEaseMusic.ReadClientRecommendationsAsync(_lifetime.Token);
            if (generation != _accountGeneration)
                return;
            RecommendedPlaylists.ItemsSource = PlaylistTiles(playlists);
            RecommendedStatus.Text = playlists.Length == 0 ? "暂无推荐歌单。" : "";
            RecommendedStatus.Visibility = playlists.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            if (generation == _accountGeneration)
                RecommendedStatus.Text = "推荐歌单暂时无法加载。点击主页可重试。";
        }
        finally
        {
            _loadingPlaylists = false;
            if (generation != _accountGeneration && !_lifetime.IsCancellationRequested)
                _ = LoadHomePlaylistsAsync();
        }
    }

    private static PlaylistTile[] PlaylistTiles(NetEaseMusic.ClientPlaylist[] playlists) =>
        playlists.Select(playlist => new PlaylistTile(playlist, Thumbnail(playlist.CoverUrl, 240))).ToArray();

    private async void OpenRecommendedPlaylist(object sender, RoutedEventArgs e)
    {
        if (_searchBusy || sender is not Button { Tag: NetEaseMusic.ClientPlaylist playlist })
            return;
        await OpenPlaylistAsync(playlist.Name, "playlist", playlist.Id, Thumbnail(playlist.CoverUrl, 240));
    }

    private async Task OpenPlaylistAsync(string heading, string operation, long id, ImageSource? cover)
    {
        if (_page == PlaylistPage && _libraryOperation == operation && _libraryPlaylistId == id &&
            (_playlistLoad is not null || _suspendedPlaylistOffset.HasValue || PlaylistTracks.ItemsSource is not null))
            return;

        CancelPlaylistLoad();
        _libraryOperation = null;
        _libraryTrackIds = null;
        _playlistTitle = heading;
        PlaylistCover.ImageSource = cover;
        PlaylistArtwork.Visibility = cover is null ? Visibility.Collapsed : Visibility.Visible;
        PlaylistTracks.ItemsSource = null;
        MoreNetEaseTracks.Visibility = Visibility.Collapsed;
        _nextLibraryOffset = null;
        UpdatePlaylistPlayAvailability();
        PlaylistPage.ScrollToHome();
        ShowPage(PlaylistPage, liked: operation == "liked");
        UpdatePageTitle();
        if (!UseClientSession)
        {
            PlaylistStatus.Text = "请在标题栏的账号面板登录网易云音乐。";
            return;
        }
        await LoadNetEaseLibraryAsync(operation, id, 0);
    }

    private async void LoadMoreNetEaseTracks(object sender, RoutedEventArgs e)
    {
        if (!_searchBusy && _playlistLoad is null && _libraryOperation is { } operation && _nextLibraryOffset is { } offset)
            await LoadNetEaseLibraryAsync(operation, _libraryPlaylistId, offset);
    }

    private Task LoadNetEaseLibraryAsync(string operation, long id, int offset) =>
        LoadPlaylistAsync(operation, id, offset, async token =>
        {
            var page = await NetEaseMusic.ReadClientLibraryAsync(operation, id, offset, token, _libraryTrackIds);
            token.ThrowIfCancellationRequested();
            _libraryTrackIds = page.TrackIds ?? _libraryTrackIds;
            return (page.Tracks, page.NextOffset);
        });

    private async Task LoadPlaylistAsync(string operation, long id, int offset,
        Func<CancellationToken, Task<(NetEaseTrack[] Tracks, int? NextOffset)>> fetch)
    {
        if (_playlistLoad is not null)
            return;
        _suspendedPlaylistOffset = null;
        if (QrPanel.Visibility == Visibility.Visible)
        {
            PlaylistStatus.Text = "请先完成或取消扫码。";
            return;
        }
        using var load = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _playlistLoad = load;
        _libraryOperation = operation;
        _libraryPlaylistId = id;
        SetPlaylistLoading(true);
        PlaylistStatus.Text = "正在加载歌曲…";
        try
        {
            var (tracks, nextOffset) = await fetch(load.Token);
            load.Token.ThrowIfCancellationRequested();
            _nextLibraryOffset = nextOffset;
            ShowNetEaseTracks(tracks, PlaylistTracks, append: offset > 0);
            MoreNetEaseTracks.Visibility = nextOffset.HasValue ? Visibility.Visible : Visibility.Collapsed;
            PlaylistStatus.Text = PlaylistTracks.Items.Count == 0 ? "暂无歌曲。" : $"已加载 {PlaylistTracks.Items.Count} 首";
        }
        catch (Exception error)
        {
            if (!load.IsCancellationRequested)
                PlaylistStatus.Text = error.Message;
        }
        finally
        {
            if (ReferenceEquals(_playlistLoad, load))
            {
                _playlistLoad = null;
                SetPlaylistLoading(false);
            }
        }
    }

    private void CancelPlaylistLoad()
    {
        _suspendedPlaylistOffset = null;
        if (_playlistLoad is not { } load)
            return;
        _playlistLoad = null;
        load.Cancel();
        SetPlaylistLoading(false);
    }

    private void SuspendPlaylistLoad()
    {
        if (_playlistLoad is null)
            return;
        int offset = _nextLibraryOffset ?? 0;
        CancelPlaylistLoad();
        _suspendedPlaylistOffset = offset;
    }

    private void ResumePlaylistLoad()
    {
        if (_lyrics.LyricsShown || _page != PlaylistPage || _lifetime.IsCancellationRequested ||
            _suspendedPlaylistOffset is not { } offset || _libraryOperation is not { } operation)
            return;
        _suspendedPlaylistOffset = null;
        if (UseClientSession)
            _ = LoadNetEaseLibraryAsync(operation, _libraryPlaylistId, offset);
        else
            PlaylistStatus.Text = "请在标题栏的账号面板登录网易云音乐。";
    }

    private void SetPlaylistLoading(bool loading)
    {
        MoreNetEaseTracks.IsEnabled = !loading && !_searchBusy;
        PlaylistSpinner.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        UpdatePlaylistPlayAvailability();
    }

    private void UpdatePlaylistPlayAvailability() => PlaylistPlayButton.IsEnabled =
        _libraryOperation is not null && UseClientSession && !_searchBusy && _playlistLoad is null &&
        (_nextLibraryOffset.HasValue || PlaylistTracks.Items.OfType<SearchRow>().Any(row => row.Track.Playable));

    private async void PlayPlaylist(object sender, RoutedEventArgs e)
    {
        if (!PlaylistPlayButton.IsEnabled || _libraryOperation is not { } operation)
            return;
        long id = _libraryPlaylistId;
        long selection = _lyrics.QueueSelection;
        int? offset = _nextLibraryOffset;
        var trackIds = _libraryTrackIds;
        var tracks = PlaylistTracks.Items.OfType<SearchRow>().Select(row => row.Track).ToList();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _playPlaylistRequest = request;
        try
        {
            await RunSearchAsync("正在读取完整歌单…", _ => _lyrics.WhilePlaybackLoadsAsync(async () =>
            {
                while (offset is { } start)
                {
                    var page = await NetEaseMusic.ReadClientLibraryAsync(operation, id, start, request.Token, trackIds);
                    request.Token.ThrowIfCancellationRequested();
                    if (selection != _lyrics.QueueSelection)
                    {
                        PlaylistStatus.Text = $"已加载 {PlaylistTracks.Items.Count} 首";
                        return;
                    }
                    trackIds = page.TrackIds ?? trackIds;
                    tracks.AddRange(page.Tracks);
                    offset = page.NextOffset;
                }
                bool started = await _lyrics.PlayNetEasePlaylistAsync(tracks, selection, request.Token);
                request.Token.ThrowIfCancellationRequested();
                RefreshPlayer();
                PlaylistStatus.Text = started ? $"播放队列 · {_lyrics.Playlist.Count} 首歌曲" : $"已加载 {PlaylistTracks.Items.Count} 首";
            }), playlist: true, cancellationToken: request.Token);
        }
        finally
        {
            _playPlaylistRequest = null;
            if (request.IsCancellationRequested)
                PlaylistStatus.Text = $"已加载 {PlaylistTracks.Items.Count} 首";
        }
    }

    private CancellationTokenSource? _playPlaylistRequest;

    private void OpenSongQueueMenu(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SearchRow { Track: var track } } row)
            return;
        e.Handled = true;
        var menu = new ContextMenu { PlacementTarget = row, Placement = PlacementMode.MousePoint };
        Reference.ReferenceMenus.Apply(menu);
        foreach (bool next in new[] { true, false })
        {
            var item = new MenuItem { Header = next ? "插播" : "添加至队列末尾", IsEnabled = track.Playable };
            item.Click += (_, _) =>
            {
                _lyrics.QueueNetEase(track, next);
                RefreshPlayer();
            };
            menu.Items.Add(item);
        }
        OpenShellMenu(menu);
    }

    private void ShowNetEaseTracks(NetEaseTrack[] tracks, ListBox? list = null, bool append = false)
    {
        list ??= SearchResults;
        var rows = tracks.Select(track => new SearchRow(track, Thumbnail(track.CoverUrl), Duration(track.DurationMs)));
        if (append && list.ItemsSource is SearchRow[] previous)
            rows = previous.Concat(rows);
        list.ItemsSource = rows.ToArray();
    }

    private async void PlaySearchResult(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox list && e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(list, source) is ListBoxItem { DataContext: SearchRow row })
            await PlayAsync(row, list == PlaylistTracks);
    }

    private async void PlaySearchResultButton(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SearchRow row } button)
        {
            bool playlist = ItemsControl.ContainerFromElement(PlaylistTracks, button) is ListBoxItem;
            (playlist ? PlaylistTracks : SearchResults).SelectedItem = row;
            await PlayAsync(row, playlist);
        }
    }

    private async void OnSearchResultsKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not ListBox { SelectedItem: SearchRow row } list)
            return;
        e.Handled = true;
        await PlayAsync(row, list == PlaylistTracks);
    }

    private async Task PlayAsync(SearchRow row, bool playlist)
    {
        if (_searchBusy || (playlist && _playlistLoad is not null))
            return;
        if (!row.Track.Playable)
        {
            (playlist ? PlaylistStatus : SearchStatus).Text = row.Track.Availability;
            return;
        }
        await RunSearchAsync($"正在载入《{row.Track.Title}》…", async token =>
        {
            bool started = await _lyrics.PlayNetEaseAsync(row.Track, token);
            RefreshPlayer();
            (playlist ? PlaylistStatus : SearchStatus).Text = started ? $"正在播放《{row.Track.Title}》" :
                $"已加载 {(playlist ? PlaylistTracks : SearchResults).Items.Count} 首";
        }, playlist);
    }

    private async Task RunSearchAsync(string message, Func<CancellationToken, Task> action, bool playlist = false,
        CancellationToken cancellationToken = default)
    {
        var status = playlist ? PlaylistStatus : SearchStatus;
        var spinner = playlist ? PlaylistSpinner : SearchSpinner;
        if (QrPanel.Visibility == Visibility.Visible)
        {
            status.Text = "请先完成或取消扫码。";
            return;
        }
        _searchBusy = true;
        SearchQuery.IsEnabled = LoginButton.IsEnabled = false;
        MoreNetEaseTracks.IsEnabled = LogoutButton.IsEnabled = false;
        UpdatePlaylistPlayAvailability();
        spinner.Visibility = Visibility.Visible;
        status.Text = message;
        try { await action(cancellationToken.CanBeCanceled ? cancellationToken : _lifetime.Token); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested || cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { status.Text = exception.Message; }
        finally
        {
            _searchBusy = false;
            SearchQuery.IsEnabled = LoginButton.IsEnabled = true;
            MoreNetEaseTracks.IsEnabled = _playlistLoad is null;
            LogoutButton.IsEnabled = true;
            UpdatePlaylistPlayAvailability();
            spinner.Visibility = Visibility.Collapsed;
        }
    }

    private static ImageSource? Thumbnail(string url, int size = 96)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || !uri.Host.EndsWith(".music.126.net", StringComparison.OrdinalIgnoreCase))
            return null;
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new UriBuilder(uri) { Scheme = "https", Port = -1, Query = $"param={size}y{size}" }.Uri;
        image.DecodePixelWidth = size;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        return image;
    }

    // Login

    private void UpdateAccountState()
    {
        bool client = NetEaseMusic.HasClientSession;
        AccountButtonText.Text = string.IsNullOrWhiteSpace(_accountName) ? "账号" : _accountName;
        LoginTitle.Text = string.IsNullOrWhiteSpace(_accountName) ? "网易云音乐" : _accountName;
        LoginDetail.Text = client
            ? _accountDetail ?? "正在确认登录状态…"
            : "用手机扫码登录后，可搜索并播放账号有权限的完整曲目。未登录时使用开放平台授权。";
        SearchSubtitle.Text = UseClientSession ? "网易云音乐 · 客户端会话" : "网易云音乐 · 开放平台";
        if (QrPanel.Visibility != Visibility.Visible)
            LoginButton.Content = client ? "重新扫码" : "扫码登录";
        LogoutButton.Visibility = client && QrPanel.Visibility != Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        if (!UseClientSession)
        {
            RecommendedPlaylists.ItemsSource = null;
            RecommendedStatus.Text = "在标题栏登录网易云音乐后可查看推荐歌单。";
            RecommendedStatus.Visibility = Visibility.Visible;
        }
        if (client && !_selfTest && !_checkingAccount && _accountDetail is null)
            _ = RefreshAccountAsync(_accountGeneration);
    }

    private async Task RefreshAccountAsync(int generation)
    {
        _checkingAccount = true;
        try
        {
            var account = await NetEaseMusic.ReadClientAccountAsync(_lifetime.Token);
            if (generation != _accountGeneration)
                return;
            _accountName = account?.Name;
            _accountSignedIn = account is not null;
            AccountPhoto.ImageSource = account is null ? null : Thumbnail(account.AvatarUrl);
            _accountDetail = account is null ? "登录已失效，请重新扫码。" : "已登录网易云音乐";
            if (account is not null)
                _ = LoadHomePlaylistsAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            if (generation == _accountGeneration)
                _accountDetail = "暂时无法确认登录状态。";
        }
        finally
        {
            _checkingAccount = false;
            if (!_lifetime.IsCancellationRequested)
                UpdateAccountState();
        }
    }

    private void OpenAccount(object sender, RoutedEventArgs e)
    {
        if (_accountExpanded)
        {
            CloseAccount(animate: true);
            return;
        }

        UpdateAccountState();
        _accountExpanded = true;
        CloseQueue(animate: false);
        CloseSound(animate: false);
        AccountButton.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        AccountSurface.IsHitTestVisible = true;
        if (AccountPopup.IsOpen)
        {
            ShellTransitions.PlayReveal(AccountSurface, MotionTokens.PopupEnter, MotionReduced);
            return;
        }

        ShellTransitions.PrepareReveal(AccountSurface, MotionReduced ? 1 : MotionTokens.PopupStartOpacity,
            MotionReduced ? 0 : -MotionTokens.PopupTravel);
        AccountPopup.IsOpen = true;
    }

    private void OnAccountOpened(object? sender, EventArgs e)
    {
        if (!AccountPopup.IsOpen || MotionReduced)
            return;
        CompositionTarget.Rendering -= PlayAccountEntrance;
        CompositionTarget.Rendering += PlayAccountEntrance;
    }

    private void PlayAccountEntrance(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= PlayAccountEntrance;
        if (AccountPopup.IsOpen && _accountExpanded)
            ShellTransitions.PlayReveal(AccountSurface, MotionTokens.PopupEnter, MotionReduced);
    }

    private void CloseAccount(bool animate)
    {
        if (!_accountExpanded && !AccountPopup.IsOpen)
            return;
        _accountExpanded = false;
        AccountButton.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
        if (!AccountPopup.IsOpen)
            return;
        CompositionTarget.Rendering -= PlayAccountEntrance;
        if (!animate || MotionReduced)
        {
            AccountPopup.IsOpen = false;
            return;
        }
        AccountSurface.IsHitTestVisible = false;
        if (AccountSurface.RenderTransform is TranslateTransform shift)
            ShellTransitions.To(shift, TranslateTransform.YProperty, -MotionTokens.PopupTravel, MotionTokens.PopupExit);
        ShellTransitions.To(AccountSurface, OpacityProperty, 0, MotionTokens.PopupExit, completed: () =>
        {
            if (!_accountExpanded)
                AccountPopup.IsOpen = false;
        });
    }

    private void OnAccountClosed(object? sender, EventArgs e)
    {
        if (AccountPopup.IsOpen)
            return;
        _accountExpanded = false;
        AccountButton.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
        CompositionTarget.Rendering -= PlayAccountEntrance;
        ShellTransitions.PrepareReveal(AccountSurface, 1, 0);
        ResetQrPanel();
    }

    private void OnAccountKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        CloseAccount(animate: true);
        AccountButton.Focus();
        e.Handled = true;
    }

    private async void LogoutAccount(object sender, RoutedEventArgs e)
    {
        if (_searchBusy || _lyrics.PlaybackLoading)
            return;
        try
        {
            await _lyrics.StopNetEaseAsync();
            NetEaseMusic.ForgetClientSession();
            ResetAccount();
            ShowNotice("已退出网易云音乐本机登录");
        }
        catch (Exception exception) { ShowNotice(exception.Message); }
    }

    private void ResetAccount()
    {
        CancelPlaylistLoad();
        _accountGeneration++;
        _accountSignedIn = null;
        _accountName = _accountDetail = null;
        AccountPhoto.ImageSource = null;
        RecommendedPlaylists.ItemsSource = null;
        PlaylistTracks.ItemsSource = null;
        _nextLibraryOffset = null;
        MoreNetEaseTracks.Visibility = Visibility.Collapsed;
        UpdatePlaylistPlayAvailability();
        ResetNetEaseResults("搜索");
        UpdateAccountState();
    }

    private void ToggleLogin(object sender, RoutedEventArgs e)
    {
        if (QrPanel.Visibility == Visibility.Visible)
            CancelLogin(sender, e);
        else
            StartLogin();
    }

    private void StartLogin()
    {
        if (_searchBusy)
            return;
        QrPanel.Visibility = Visibility.Visible;
        LoginButton.Content = "收起";
        LogoutButton.Visibility = Visibility.Collapsed;
        RefreshQr(this, new RoutedEventArgs());
    }

    private void CancelLogin(object sender, RoutedEventArgs e) => ResetQrPanel();

    private void ResetQrPanel()
    {
        _loginLifetime?.Cancel();
        ShellTransitions.Set(QrImage, OpacityProperty, 1);
        QrImage.Source = null;
        QrPanel.Visibility = Visibility.Collapsed;
        UpdateAccountState();
    }

    private async void RefreshQr(object sender, RoutedEventArgs e)
    {
        _loginLifetime?.Cancel();
        var lifetime = _loginLifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = lifetime.Token;
        QrRetry.IsEnabled = false;
        QrImage.Source = null;
        ShellTransitions.Set(QrImage, OpacityProperty, 1);
        QrSpinner.Visibility = Visibility.Visible;
        QrStatus.Text = "正在获取二维码…";
        try
        {
            var qr = await NetEaseMusic.CreateClientQrAsync(token);
            token.ThrowIfCancellationRequested();
            QrImage.Source = Artwork.Decode(qr.Image);
            QrSpinner.Visibility = Visibility.Collapsed;
            ShellTransitions.Reveal(QrImage, .8, 0, MotionTokens.ArtworkReveal, MotionReduced);
            QrStatus.Text = "打开网易云音乐 App，扫描二维码并在手机上确认登录。";
            while (true)
            {
                await Task.Delay(1800, token);
                int state = await NetEaseMusic.PollClientQrAsync(qr.Key, token);
                token.ThrowIfCancellationRequested();
                if (state == 803)
                {
                    QrPanel.Visibility = Visibility.Collapsed;
                    QrImage.Source = null;
                    ResetAccount();
                    SearchStatus.Text = "已登录客户端会话，可以搜索歌曲。";
                    ShowNotice("网易云音乐登录成功");
                    return;
                }
                if (state == 800)
                {
                    QrStatus.Text = "二维码已过期，请刷新。";
                    ShellTransitions.Set(QrImage, OpacityProperty, .25);
                    return;
                }
                if (state is not (801 or 802))
                {
                    QrStatus.Text = "网易云暂未接受登录，请刷新后重试。";
                    return;
                }
                if (state == 802)
                    QrStatus.Text = "已扫码，请在手机上确认。";
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (ReferenceEquals(_loginLifetime, lifetime) && !token.IsCancellationRequested)
                QrStatus.Text = exception.Message;
        }
        finally
        {
            if (ReferenceEquals(_loginLifetime, lifetime))
            {
                _loginLifetime = null;
                QrSpinner.Visibility = Visibility.Collapsed;
                QrRetry.IsEnabled = !token.IsCancellationRequested;
            }
            lifetime.Dispose();
        }
    }
}
