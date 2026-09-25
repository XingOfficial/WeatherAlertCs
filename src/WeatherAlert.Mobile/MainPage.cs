using WeatherAlert.Core;
using Microsoft.Maui.Controls;

namespace WeatherAlert.Mobile;

public class MainPage : ContentPage
{
    // 收藏存储（与云同步共用 Core FavStore）
    internal static readonly FavStore Store =
        new(Microsoft.Maui.Storage.FileSystem.AppDataDirectory);

    private readonly Label _statLabel = new() { FontSize = 13 };
    private readonly SearchBar _searchBar = new() { Placeholder = "搜索地区 / 类型", Margin = new Thickness(12, 4) };
    private readonly VerticalStackLayout _listHost = new() { Spacing = 6, Padding = new Thickness(8, 4) };
    private readonly Button _moreButton = new() { Text = "加载更多", Margin = new Thickness(12, 8) };

    private readonly List<Alert> _all = [];
    private int _pageNo = 1;
    private string _province = "", _type = "", _level = "";
    private bool _favOnly;

    public MainPage()
    {
        Title = "天气预警";
        var filterItem = new ToolbarItem { Text = "筛选" };
        filterItem.Clicked += OnFilter;
        var favItem = new ToolbarItem { Text = "收藏" };
        favItem.Clicked += OnToggleFavFilter;
        var syncItem = new ToolbarItem { Text = "账号" };
        syncItem.Clicked += OnAccount;
        ToolbarItems.Add(filterItem);
        ToolbarItems.Add(favItem);
        ToolbarItems.Add(syncItem);

        var frame = new Frame
        {
            BackgroundColor = Color.FromArgb("#E8F0FE"),
            CornerRadius = 10,
            Margin = new Thickness(12, 8),
            Padding = 12,
            Content = _statLabel,
        };

        _searchBar.TextChanged += (_, _) => Render();
        _moreButton.Clicked += async (_, _) => await LoadMore();

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Children = { frame, _searchBar, _listHost, _moreButton },
            },
        };

        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        _statLabel.Text = "加载中…";
        _all.Clear();
        _pageNo = 1;
        try
        {
            var page = await AlertApi.FetchAlertsAsync(1, 20, _province, _type, _level);
            _all.AddRange(page.Alerts);
            if (page.Stat is { } st)
                _statLabel.Text = $"全国生效预警 {st.Total} 条　红 {st.Red} · 橙 {st.Orange} · 黄 {st.Yellow} · 蓝 {st.Blue}\n数据来源：中央气象台 nmc.cn";
        }
        catch (Exception ex)
        {
            _statLabel.Text = $"加载失败：{ex.Message}（下拉工具栏重试或检查网络）";
        }
        Render();
    }

    private async Task LoadMore()
    {
        try
        {
            var page = await AlertApi.FetchAlertsAsync(_pageNo + 1, 20, _province, _type, _level);
            if (page.Alerts.Count == 0)
            {
                _moreButton.Text = "没有更多了";
                _moreButton.IsEnabled = false;
                return;
            }
            _pageNo++;
            _all.AddRange(page.Alerts);
            Render();
        }
        catch { }
    }

    private void Render()
    {
        _listHost.Clear();
        var q = _searchBar.Text ?? "";
        var visible = _all.Where(a =>
            (q.Length == 0 || a.Title.Contains(q) || a.Location.Contains(q) || a.SignalType.Contains(q)) &&
            (!_favOnly || IsFav(a))).ToList();

        foreach (var a in visible) _listHost.Add(BuildCard(a));

        if (visible.Count == 0)
            _listHost.Add(new Label
            {
                Text = _favOnly ? "暂无收藏的预警" : "当前条件下暂无预警信息",
                HorizontalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 40),
                TextColor = Colors.Gray,
            });
    }

    private View BuildCard(Alert a)
    {
        var color = LevelColor(a);
        var title = new Label
        {
            Text = a.Title,
            FontAttributes = FontAttributes.Bold,
            FontSize = 15,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 2,
        };
        var badge = new Label
        {
            Text = $"{a.SignalLevel}预警",
            TextColor = Colors.White,
            BackgroundColor = color,
            FontSize = 11,
            Padding = new Thickness(6, 2),
        };
        var fav = new Label
        {
            Text = IsFav(a) ? "已收藏" : "",
            TextColor = Colors.Orange,
            FontSize = 16,
            VerticalOptions = LayoutOptions.Center,
        };
        var meta = new Label
        {
            Text = $"{a.Location} | {a.IssueTime}",
            FontSize = 12,
            TextColor = Colors.Gray,
        };

        var card = new Frame
        {
            Padding = 0,
            Margin = 0,
            CornerRadius = 10,
            HasShadow = false,
            BorderColor = color,
            BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Dark
                ? Color.FromArgb("#222222") : Colors.White,
            Content = new StackLayout
            {
                Padding = 10,
                Children =
                {
                    new StackLayout
                    {
                        Orientation = StackOrientation.Horizontal,
                        Children = { title, badge, fav },
                    },
                    meta,
                },
            },
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await Navigation.PushAsync(new DetailPage(a, IsFav(a), OnFavChanged));
        card.GestureRecognizers.Add(tap);
        return card;
    }

    private void OnFavChanged() => Render();

    private static bool IsFav(Alert a) => Store.IsFavorite(a.Id);

    private async void OnAccount(object? sender, EventArgs e)
    {
        var dir = Microsoft.Maui.Storage.FileSystem.AppDataDirectory;
        var session = AccountSync.Load(dir);

        if (session.Valid)
        {
            var act = await DisplayActionSheet($"账号: {session.User}", "取消", null, "同步收藏", "退出登录");
            if (act == "同步收藏")
            {
                try
                {
                    var count = await AccountSync.SyncNowAsync(session.Server, session, Store);
                    await DisplayAlert("账号同步", $"同步完成，共 {count} 条收藏", "确定");
                }
                catch (Exception ex)
                {
                    await DisplayAlert("同步失败", ex.Message, "确定");
                }
                Render();
            }
            else if (act == "退出登录")
            {
                AccountSync.Save(dir, new AccountSession());
                await DisplayAlert("账号", "已退出登录", "确定");
            }
            return;
        }

        var choice = await DisplayActionSheet("账号", "取消", null,
            "登录（用户名+密码）", "登录（邮箱验证码）", "注册（用户名+密码）", "注册（邮箱验证码）");
        try
        {
            AccountSession? s = null;
            switch (choice)
            {
                case "登录（用户名+密码）":
                    {
                        var ln = await DisplayPromptAsync("登录", "用户名：");
                        if (ln is null) return;
                        var lp = await DisplayPromptAsync("登录", "密码：");
                        if (lp is null) return;
                        s = await AccountSync.LoginAsync("", ln, lp, "", "");
                        break;
                    }
                case "登录（邮箱验证码）":
                    {
                        var le = await DisplayPromptAsync("登录", "邮箱：");
                        if (le is null) return;
                        var lc = await SendCodeAndPrompt(le);
                        if (lc is null) return;
                        s = await AccountSync.LoginAsync("", "", "", le, lc);
                        break;
                    }
                case "注册（用户名+密码）":
                    {
                        var rn = await DisplayPromptAsync("注册", "用户名（字母/数字/_.@-）：");
                        if (rn is null) return;
                        var rp = await DisplayPromptAsync("注册", "密码：");
                        if (rp is null) return;
                        s = await AccountSync.SignupAsync("", "namepassword", rn, rp, "", "");
                        break;
                    }
                case "注册（邮箱验证码）":
                    {
                        var re = await DisplayPromptAsync("注册", "邮箱：");
                        if (re is null) return;
                        var rac = await SendCodeAndPrompt(re);
                        if (rac is null) return;
                        s = await AccountSync.SignupAsync("", "emailauthcode", "", "", re, rac);
                        break;
                    }
                default:
                    return;
            }
            AccountSync.Save(dir, s);
            var count = await AccountSync.SyncNowAsync(s.Server, s, Store);
            await DisplayAlert("账号", $"已登录 {s.User}，同步完成共 {count} 条收藏", "确定");
        }
        catch (Exception ex)
        {
            await DisplayAlert("失败", ex.Message, "确定");
        }
        Render();
    }

    private async Task<string?> SendCodeAndPrompt(string email)
    {
        await AccountSync.SendAuthCodeAsync("", email);
        return await DisplayPromptAsync("验证码",
            $"验证码已发送至 {email}\n（Termux 无邮件通道时，验证码由服务端直接回显在浏览器控制台/响应中）");
    }

    private void OnToggleFavFilter(object? sender, EventArgs e)
    {
        _favOnly = !_favOnly;
        DisplayAlert("提示", _favOnly ? "已切换为只看收藏" : "已显示全部", "确定");
        Render();
    }

    private async void OnFilter(object? sender, EventArgs e)
    {
        var cat = await DisplayActionSheet("筛选预警", "取消", null, "省份", "预警类型", "预警等级", "重置筛选");
        switch (cat)
        {
            case "省份":
                var p = await DisplayActionSheet("选择省份", "取消", null, Provinces.All);
                if (p is not null and not "取消") { _province = p; await LoadAsync(); }
                break;
            case "预警类型":
                var t = await DisplayActionSheet("选择类型", "取消", null, AlertTypes.All);
                if (t is not null and not "取消") { _type = t; await LoadAsync(); }
                break;
            case "预警等级":
                var l = await DisplayActionSheet("选择等级", "取消", null, AlertLevels.All);
                if (l is not null and not "取消") { _level = l; await LoadAsync(); }
                break;
            case "重置筛选":
                _province = _type = _level = "";
                await LoadAsync();
                break;
        }
    }

    internal static Color LevelColor(Alert a) => a.NormalizedLevel switch
    {
        "红色" => Color.FromArgb("#E53935"),
        "橙色" => Color.FromArgb("#F57C00"),
        "黄色" => Color.FromArgb("#FBC02D"),
        "蓝色" => Color.FromArgb("#1E88E5"),
        _ => Color.FromArgb("#757575"),
    };
}
