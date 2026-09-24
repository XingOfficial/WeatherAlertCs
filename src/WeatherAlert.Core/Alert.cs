using System.Text.RegularExpressions;

namespace WeatherAlert.Core;

public sealed record Alert(
    string Id,
    string Title,
    string SignalType,
    string SignalLevel,
    string Location,
    string IssueTime)
{
    private static readonly Regex TitlePattern =
        new(@"^(.+?)发布(.+?)(蓝色|黄色|橙色|红色)预警信号?$", RegexOptions.Compiled);

    public string? NormalizedLevel =>
        new[] { "蓝色", "黄色", "橙色", "红色" }.FirstOrDefault(l => SignalLevel.Contains(l));

    public int LevelWeight => NormalizedLevel switch
    {
        "蓝色" => 0,
        "黄色" => 1,
        "橙色" => 2,
        "红色" => 3,
        _ => 0,
    };

    public static Alert FromListJson(string id, string title, string issueTime)
    {
        var m = TitlePattern.Match(title);
        return new Alert(
            Id: id,
            Title: title,
            SignalType: m.Success ? m.Groups[2].Value : "",
            SignalLevel: m.Success ? m.Groups[3].Value : "",
            Location: m.Success ? m.Groups[1].Value : "气象台",
            IssueTime: issueTime);
    }
}

public sealed record AlertStat(int Total, int Red, int Orange, int Yellow, int Blue);

public sealed record AlertsPage(IReadOnlyList<Alert> Alerts, AlertStat? Stat);

public static class Provinces
{
    public static readonly string[] All =
    [
        "北京市", "天津市", "河北省", "山西省", "内蒙古自治区",
        "辽宁省", "吉林省", "黑龙江省", "上海市", "江苏省",
        "浙江省", "安徽省", "福建省", "江西省", "山东省",
        "河南省", "湖北省", "湖南省", "广东省", "广西壮族自治区",
        "海南省", "重庆市", "四川省", "贵州省", "云南省",
        "西藏自治区", "陕西省", "甘肃省", "青海省", "宁夏回族自治区",
        "新疆维吾尔自治区", "台湾省", "香港", "澳门",
    ];
}

public static class AlertTypes
{
    public static readonly string[] All =
    [
        "暴雨", "台风", "暴雪", "寒潮", "大风", "沙尘暴",
        "高温", "干旱", "雷电", "冰雹", "霜冻", "大雾", "道路结冰",
    ];
}

public static class AlertLevels
{
    public static readonly string[] All = ["蓝色", "黄色", "橙色", "红色"];
}
