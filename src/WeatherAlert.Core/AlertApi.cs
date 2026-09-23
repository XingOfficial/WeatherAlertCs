using System.Net.Http.Headers;
using System.Text.Json;

namespace WeatherAlert.Core;

/// <summary>中央气象台（nmc.cn）公开预警数据访问</summary>
public static class AlertApi
{
    private const string ListUrl = "https://www.nmc.cn/rest/findAlarm";
    private const string PageUrl = "https://www.nmc.cn/publish/alarm";

    public sealed class ApiException(string message) : Exception(message);

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 Chrome/120 Mobile Safari/537.36");
        // 该 Accept 头是接口正常返回 JSON 的关键
        c.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        c.DefaultRequestHeaders.Referrer = new Uri("https://www.nmc.cn/publish/alarm.html");
        return c;
    }

    /// <summary>查询预警列表：支持省份/类型/等级筛选，按等级（红>橙>黄>蓝）排序</summary>
    public static async Task<AlertsPage> FetchAlertsAsync(
        int pageNo = 1,
        int pageSize = 20,
        string province = "",
        string signalType = "",
        string signalLevel = "",
        CancellationToken ct = default)
    {
        var q = $"pageNo={pageNo}&pageSize={pageSize}" +
                $"&signaltype={Uri.EscapeDataString(signalType)}" +
                $"&signallevel={Uri.EscapeDataString(signalLevel)}" +
                $"&province={Uri.EscapeDataString(province)}" +
                $"&_={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        using var doc = await GetJsonAsync($"{ListUrl}?{q}", ct);
        var root = doc.RootElement;
        if (root.TryGetProperty("code", out var code) && code.GetInt32() != 0)
            throw new ApiException(root.TryGetProperty("msg", out var msg) ? msg.GetString() : "接口异常");

        var data = root.GetProperty("data");
        var alerts = new List<Alert>();
        if (data.TryGetProperty("page", out var page) &&
            page.TryGetProperty("list", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var o in list.EnumerateArray())
            {
                alerts.Add(Alert.FromListJson(
                    o.TryGetProperty("alertid", out var id) ? id.GetString() ?? "" : "",
                    o.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                    o.TryGetProperty("issuetime", out var tm) ? tm.GetString() ?? "" : ""));
            }
        }

        var stat = ParseStat(data, page);
        return new AlertsPage(alerts.OrderByDescending(a => a.LevelWeight).ToList(), stat);
    }

    private static AlertStat? ParseStat(JsonElement data, JsonElement page)
    {
        if (!data.TryGetProperty("stat", out var stat))
            return null;

        int Sum(string key) =>
            new[] { "province", "city", "county" }
                .Select(stat.GetProperty)
                .Where(s => s.ValueKind == JsonValueKind.Object)
                .Sum(s => s.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0);

        var total = page.ValueKind == JsonValueKind.Object &&
                    page.TryGetProperty("count", out var c) && c.ValueKind == JsonValueKind.Number
            ? c.GetInt32() : 0;
        return new AlertStat(total, Sum("r"), Sum("o"), Sum("y"), Sum("b"));
    }

    /// <summary>抓取预警详情页并解析正文与防御指南</summary>
    public static async Task<string> FetchDetailAsync(string alertId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(alertId)) throw new ApiException("预警ID为空");
        var html = await Http.GetStringAsync($"{PageUrl}/{alertId}.html", ct);
        var blocks = Regex.Matches(
                html,
                "<div[^>]*id=[\"']?alarmtext[\"']?[^>]*>(.*?)</div>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Select(m => m.Groups[1].Value)
            .ToList();
        if (blocks.Count == 0) throw new ApiException("未解析到预警内容");
        return string.Join("\n\n", blocks.Select(b => StripHtml(b).Trim()));
    }

    /// <summary>预警详情页 URL</summary>
    public static string DetailUrl(string alertId) => $"{PageUrl}/{alertId}.html";

    private static async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        var text = await Http.GetStringAsync(url, ct);
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            throw new ApiException("接口返回格式异常（可能被网关拦截）");
        }
    }

    private static string StripHtml(string s) =>
        Regex.Replace(
            Regex.Replace(s, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase)
                .Pipe(x => Regex.Replace(x, "<[^>]+>", "")),
            @"\n{3,}", "\n\n")
        .Replace("&nbsp;", " ")
        .Replace("&lt;", "<")
        .Replace("&gt;", ">")
        .Replace("&amp;", "&")
        .Replace("&quot;", "\"");
}

internal static class PipeExtensions
{
    public static T Pipe<T>(this T value, Func<T, T> f) => f(value);
}
