using WeatherAlert.Core;

namespace WeatherAlert.Cli;

internal static class Program
{
    private const string Reset = "\x1b[0m";
    private static string Color(int c) => $"\x1b[38;5;{c}m";

    private static async Task<int> Main(string[] args)
    {
        string province = "", type = "", level = "", search = "", detailId = "";
        int page = 1, pageSize = 20;

        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : "";
            switch (args[i])
            {
                case "--province" or "-p": province = Next(); break;
                case "--type" or "-t": type = Next(); break;
                case "--level" or "-l": level = Next(); break;
                case "--search" or "-s": search = Next(); break;
                case "--page": int.TryParse(Next(), out page); break;
                case "--size": int.TryParse(Next(), out pageSize); break;
                case "--detail" or "-d": detailId = Next(); break;
                case "--help" or "-h":
                    PrintHelp();
                    return 0;
                default:
                    Console.Error.WriteLine($"未知参数: {args[i]}（--help 查看用法）");
                    return 1;
            }
        }

        try
        {
            if (!string.IsNullOrEmpty(detailId))
                return await ShowDetail(detailId);
            return await ShowList(province, type, level, search, page, pageSize);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"出错: {ex.Message}");
            return 2;
        }
    }

    private static async Task<int> ShowDetail(string id)
    {
        var content = await AlertApi.FetchDetailAsync(id);
        Console.WriteLine(content);
        Console.WriteLine($"\n原文: {AlertApi.DetailUrl(id)}");
        return 0;
    }

    private static async Task<int> ShowList(
        string province, string type, string level, string search, int page, int pageSize)
    {
        var result = await AlertApi.FetchAlertsAsync(page, pageSize, province, type, level);
        var alerts = result.Alerts;
        if (!string.IsNullOrEmpty(search))
            alerts = alerts.Where(a =>
                a.Title.Contains(search) || a.Location.Contains(search) ||
                a.SignalType.Contains(search)).ToList();

        var filters = new[] { province, type, level, search }.Where(s => !string.IsNullOrEmpty(s));
        var title = $"天气预警（中央气象台） 第 {page} 页";
        if (filters.Any()) title += " | 筛选: " + string.Join(" / ", filters);
        Console.WriteLine(title);
        Console.WriteLine(new string('─', 64));

        if (result.Stat is { } st)
        {
            Console.WriteLine(
                $"全国生效: {Color(15)}{st.Total}{Reset} 条  " +
                $"{Color(196)}红 {st.Red}{Reset}  " +
                $"{Color(208)}橙 {st.Orange}{Reset}  " +
                $"{Color(220)}黄 {st.Yellow}{Reset}  " +
                $"{Color(39)}蓝 {st.Blue}{Reset}");
            Console.WriteLine(new string('─', 64));
        }

        if (alerts.Count == 0)
        {
            Console.WriteLine("当前条件下暂无预警信息");
            return 0;
        }

        foreach (var a in alerts)
        {
            var lv = a.NormalizedLevel;
            var c = lv switch
            {
                "红色" => 196, "橙色" => 208, "黄色" => 220, "蓝色" => 39, _ => 245,
            };
            Console.WriteLine(
                $"{Color(c)}■{Reset} {a.Title}\n" +
                $"  {a.Location} | {a.IssueTime} | {Color(c)}{a.SignalType}{a.SignalLevel}预警{Reset}\n" +
                $"  详情: weather-alert --detail {a.Id}");
        }

        Console.WriteLine(new string('─', 64));
        Console.WriteLine("翻页: --page 2 · 查看更多用法: --help");
        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            天气预警查询命令行工具（数据：中央气象台 nmc.cn）

            用法:
              weather-alert [选项]

            选项:
              -p, --province <省份>   按省份筛选，如 广东省、北京市
              -t, --type <类型>       按预警类型筛选，如 暴雨、台风、高温
              -l, --level <等级>      按等级筛选: 蓝色/黄色/橙色/红色
              -s, --search <关键词>   本地关键词过滤（标题/地区/类型）
                  --page <N>          页码，默认 1
                  --size <N>          每页条数，默认 20
              -d, --detail <预警ID>   查看某条预警详情与防御指南
              -h, --help              显示帮助

            示例:
              weather-alert
              weather-alert -p 湖南省 -t 暴雨 -l 红色
              weather-alert -s 台风 --page 2
              weather-alert --detail 46902341600000_20260923163143
            """);
    }
}
