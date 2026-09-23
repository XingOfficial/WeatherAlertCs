using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using WeatherAlert.Core;

namespace WeatherAlert.Gui;

/// <summary>主窗口：统计概览 + 筛选/搜索工具栏 + 列表/详情双栏</summary>
public class MainWindow : Window
{
    private readonly ListBox _list = new() { MinWidth = 380 };
    private readonly TextBox _search = new() { Watermark = "搜索地区 / 类型", Width = 180 };
    private readonly ComboBox _province = Combo("省份");
    private readonly ComboBox _type = Combo("类型");
    private readonly ComboBox _level = Combo("等级");
    private readonly ToggleButton _favOnly = new() { Content = "只看收藏" };
    private readonly TextBlock _statText = new();
    private readonly Button _favButton = new() { Content = "☆ 收藏" };
    private readonly Button _openButton = new() { Content = "浏览器打开" };
    private readonly TextBlock _detailTitle = new()
    { Text = "选择左侧预警查看详情", FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, FontSize = 16 };
    private readonly TextBlock _detailMeta = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.7 };
    private readonly TextBlock _detailContent = new() { TextWrapping = TextWrapping.Wrap };

    private readonly List<Alert> _all = [];
    private Alert? _selected;
    private readonly FavStore _favs = new();

    public MainWindow()
    {
        _province.ItemsSource = new[] { "全部" }.Concat(Provinces.All).ToArray();
        _type.ItemsSource = new[] { "全部" }.Concat(AlertTypes.All).ToArray();
        _level.ItemsSource = new[] { "全部" }.Concat(AlertLevels.All).ToArray();
        _province.SelectedIndex = _type.SelectedIndex = _level.SelectedIndex = 0;

        _list.ItemTemplate = new FuncDataTemplate<Alert>((a, _) => BuildItem(a!));

        var statBar = new Border
        {
            Padding = new Thickness(10),
            Background = new SolidColorBrush(Color.Parse("#E8F0FE")),
            Child = _statText,
        };
        DockPanel.SetDock(statBar, Dock.Top);

        var refreshButton = new Button { Content = "刷新" };
        refreshButton.Click += async (_, _) => await LoadAsync();

        var toolbar = new Border
        {
            Padding = new Thickness(10, 6),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { _search, _province, _type, _level, _favOnly, refreshButton },
            },
        };
        DockPanel.SetDock(toolbar, Dock.Top);

        var detailPane = new ScrollViewer
        {
            Content = new StackPanel
            {
                Spacing = 10,
                Margin = new Thickness(14),
                Children =
                {
                    _detailTitle, _detailMeta,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children = { _favButton, _openButton },
                    },
                    new Separator(),
                    _detailContent,
                },
            },
        };
        Grid.SetColumn(detailPane, 2);

        var splitter = new GridSplitter { Background = Brushes.Transparent };
        Grid.SetColumn(splitter, 1);

        var mainGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*, 8, 1.2*"),
            Children = { _list, splitter, detailPane },
        };

        Content = new DockPanel
        {
            Children = { statBar, toolbar, mainGrid },
        };

        _list.SelectionChanged += async (_, _) =>
        {
            if (_list.SelectedItem is Alert a) await SelectAsync(a);
        };
        _search.TextChanged += (_, _) => ApplyFilter();
        _favOnly.IsCheckedChanged += (_, _) => ApplyFilter();
        foreach (var c in new[] { _province, _type, _level })
            c.SelectionChanged += async (_, _) => await LoadAsync();
        _favButton.Click += (_, _) => ToggleFavorite();
        _openButton.Click += (_, _) => OpenBrowser();

        _ = LoadAsync();
    }

    private static ComboBox Combo(string header) => new()
    {
        Width = 130,
        PlaceholderText = header,
    };

    private Control BuildItem(Alert a)
    {
        var color = new SolidColorBrush(LevelColor(a));
        return new Border
        {
            BorderThickness = new Thickness(4, 0, 0, 0),
            BorderBrush = color,
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 0, 4, 4),
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = a.Title,
                                FontWeight = FontWeight.SemiBold,
                                TextWrapping = TextWrapping.Wrap,
                                MaxLines = 2,
                            },
                            new Border
                            {
                                Background = color,
                                CornerRadius = new CornerRadius(3),
                                Padding = new Thickness(6, 2),
                                VerticalAlignment = VerticalAlignment.Center,
                                Child = new TextBlock
                                { Text = $"{a.SignalLevel}", Foreground = Brushes.White, FontSize = 11 },
                            },
                            FavMark(a),
                        },
                    },
                    new TextBlock
                    {
                        Text = $"{a.Location} | {a.IssueTime}",
                        Opacity = 0.6,
                        FontSize = 12,
                    },
                },
            },
        };
    }

    private Control FavMark(Alert a) => new TextBlock
    {
        Text = _favs.IsFavorite(a.Id) ? "★" : "",
        Foreground = Brushes.Orange,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Color LevelColor(Alert a) => a.NormalizedLevel switch
    {
        "红色" => Color.Parse("#E53935"),
        "橙色" => Color.Parse("#F57C00"),
        "黄色" => Color.Parse("#FBC02D"),
        "蓝色" => Color.Parse("#1E88E5"),
        _ => Color.Parse("#757575"),
    };

    private async Task LoadAsync()
    {
        _statText.Text = "加载中…";
        try
        {
            var page = await AlertApi.FetchAlertsAsync(
                1, 20,
                Sel(_province), Sel(_type), Sel(_level));
            _all.Clear();
            _all.AddRange(page.Alerts);
            if (page.Stat is { } st)
                _statText.Text = $"全国生效预警：{st.Total} 条    红 {st.Red} · 橙 {st.Orange} · 黄 {st.Yellow} · 蓝 {st.Blue}    （数据：中央气象台 nmc.cn）";
            ApplyFilter();
        }
        catch (Exception ex)
        {
            _statText.Text = $"加载失败：{ex.Message}";
        }
    }

    private static string Sel(ComboBox c) =>
        c.SelectedItem as string is { Length: > 0 } s && s != "全部" ? s : "";

    private void ApplyFilter()
    {
        var q = _search.Text ?? "";
        var visible = _all.Where(a =>
            (q.Length == 0 || a.Title.Contains(q) || a.Location.Contains(q) || a.SignalType.Contains(q)) &&
            (!_favOnly.IsChecked ?? false || _favs.IsFavorite(a.Id))).ToList();
        _list.ItemsSource = visible;
    }

    private async Task SelectAsync(Alert a)
    {
        _selected = a;
        _detailTitle.Text = a.Title;
        _detailMeta.Text = $"{a.Location} · {a.IssueTime} · {a.SignalType}{a.SignalLevel}预警";
        _detailContent.Text = "内容加载中…";
        UpdateFavButton();
        try
        {
            _detailContent.Text = await AlertApi.FetchDetailAsync(a.Id);
        }
        catch (Exception ex)
        {
            _detailContent.Text = $"内容加载失败：{ex.Message}";
        }
    }

    private void ToggleFavorite()
    {
        if (_selected is null) return;
        _favs.Toggle(_selected.Id);
        UpdateFavButton();
        ApplyFilter();
    }

    private void UpdateFavButton() =>
        _favButton.Content = _selected != null && _favs.IsFavorite(_selected.Id) ? "★ 已收藏" : "☆ 收藏";

    private void OpenBrowser()
    {
        if (_selected is null) return;
        var url = AlertApi.DetailUrl(_selected.Id);
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else if (OperatingSystem.IsLinux())
                Process.Start("xdg-open", url);
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", url);
        }
        catch { /* 忽略打开失败 */ }
    }

    private static ReactiveCommand ReactiveCommand(Func<Task> f) => new(_ => f());
}

/// <summary>极简异步命令</summary>
public sealed class ReactiveCommand : System.Windows.Input.ICommand
{
    private readonly Func<object?, Task> _execute;
    public ReactiveCommand(Func<object?, Task> execute) => _execute = execute;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => true;
    public async void Execute(object? parameter) => await _execute(parameter);
}

/// <summary>收藏持久化（JSON 文件）</summary>
public sealed class FavStore
{
    private readonly string _path;
    private HashSet<string> _ids;

    public FavStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".weather-alert");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "favorites.json");
        _ids = File.Exists(_path)
            ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(_path)) ?? []
            : [];
    }

    public bool IsFavorite(string id) => _ids.Contains(id);

    public void Toggle(string id)
    {
        if (!_ids.Add(id)) _ids.Remove(id);
        File.WriteAllText(_path, JsonSerializer.Serialize(_ids));
    }
}
