using WeatherAlert.Core;
using Microsoft.Maui.Controls;

namespace WeatherAlert.Mobile;

/// <summary>预警详情页：正文 + 防御指南 + 收藏 / 分享 / 查看原文</summary>
public class DetailPage : ContentPage
{
    private readonly Alert _alert;
    private readonly Action _onFavChanged;
    private readonly Label _favLabel;
    private readonly Label _content;

    public DetailPage(Alert alert, bool favorite, Action onFavChanged)
    {
        _alert = alert;
        _onFavChanged = onFavChanged;
        Title = "预警详情";

        var color = MainPage.LevelColor(alert);

        var shareItem = new ToolbarItem { Text = "分享" };
        shareItem.Clicked += async (_, _) => await ShareAsync();
        ToolbarItems.Add(shareItem);
        var openItem = new ToolbarItem { Text = "原文" };
        openItem.Clicked += async (_, _) =>
            await Microsoft.Maui.Essentials.Browser.OpenAsync(AlertApi.DetailUrl(alert.Id));
        ToolbarItems.Add(openItem);

        _favLabel = new Label
        {
            Text = favorite ? "★ 已收藏（点击切换）" : "☆ 收藏（点击切换）",
            TextColor = Colors.Orange,
            FontSize = 14,
            HorizontalOptions = LayoutOptions.Start,
        };
        var favTap = new TapGestureRecognizer();
        favTap.Tapped += (_, _) => ToggleFav();
        _favLabel.GestureRecognizers.Add(favTap);

        _content = new Label { Text = "内容加载中…", LineBreakMode = LineBreakMode.WordWrap, FontSize = 14 };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 16,
                Spacing = 10,
                Children =
                {
                    new StackLayout
                    {
                        Orientation = StackOrientation.Horizontal,
                        Spacing = 12,
                        Children =
                        {
                            new Frame
                            {
                                Padding = 14,
                                CornerRadius = 12,
                                BackgroundColor = color,
                                Content = new Label { Text = "⚠", TextColor = Colors.White, FontSize = 26 },
                            },
                            new VerticalStackLayout
                            {
                                new Label
                                {
                                    Text = $"{alert.SignalType}{alert.SignalLevel}预警",
                                    FontSize = 19,
                                    FontAttributes = FontAttributes.Bold,
                                },
                                new Label { Text = alert.Location, TextColor = Colors.Gray, FontSize = 13 },
                            },
                        },
                    },
                    new Label { Text = alert.Title, FontSize = 14 },
                    new Label { Text = $"发布时间：{alert.IssueTime}", TextColor = Colors.Gray, FontSize = 13 },
                    _favLabel,
                    new BoxView { HeightRequest = 1, BackgroundColor = Colors.LightGray },
                    new Label { Text = "预警内容", FontAttributes = FontAttributes.Bold, FontSize = 15 },
                    _content,
                    new Label
                    {
                        Text = "数据来源：中央气象台 nmc.cn",
                        FontSize = 12,
                        TextColor = Colors.Gray,
                        HorizontalOptions = LayoutOptions.Center,
                        Margin = new Thickness(0, 20, 0, 0),
                    },
                },
            },
        };

        _ = LoadContent();
    }

    private async Task LoadContent()
    {
        try
        {
            _content.Text = await AlertApi.FetchDetailAsync(_alert.Id);
        }
        catch (Exception ex)
        {
            _content.Text = $"内容加载失败：{ex.Message}";
        }
    }

    private void ToggleFav()
    {
        var key = "fav_" + _alert.Id;
        var now = !Microsoft.Maui.Essentials.Preferences.Get(key, false);
        Microsoft.Maui.Essentials.Preferences.Set(key, now);
        _favLabel.Text = now ? "★ 已收藏（点击切换）" : "☆ 收藏（点击切换）";
        _onFavChanged();
    }

    private async Task ShareAsync()
    {
        var text = $"【天气预警】{_alert.Title}\n发布时间：{_alert.IssueTime}\n\n" +
                   $"{(_content.Text?.Length > 300 ? _content.Text[..300] + "…" : _content.Text)}\n\n" +
                   "—— 来自「天气预警」App（中央气象台数据）";
        await Microsoft.Maui.Essentials.Share.RequestAsync(
            new Microsoft.Maui.Essentials.ShareTextRequest { Text = text, Title = "分享预警" });
    }
}
