using System.Text;
using System.Text.Json;

namespace WeatherAlert.Core;

/// <summary>收藏持久化（JSON 文件，各平台传入自己的存储目录）</summary>
public sealed class FavStore
{
    private readonly string _path;
    private HashSet<string> _ids;

    public FavStore(string dir)
    {
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "favorites.json");
        _ids = File.Exists(_path)
            ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(_path)) ?? []
            : [];
    }

    public IReadOnlyCollection<string> Ids => _ids;

    public bool IsFavorite(string id) => _ids.Contains(id);

    public bool Toggle(string id)
    {
        var added = _ids.Add(id);
        if (!added) _ids.Remove(id);
        Save();
        return added;
    }

    public void Replace(IEnumerable<string> ids)
    {
        _ids = new HashSet<string>(ids);
        Save();
    }

    private void Save() => File.WriteAllText(_path, JsonSerializer.Serialize(_ids));
}

/// <summary>云同步配置（服务器地址 + 同步码）</summary>
public sealed class SyncConfig
{
    public string Server { get; set; } = "";
    public string Code { get; set; } = "";

    public bool Enabled =>
        Server.StartsWith("http://") || Server.StartsWith("https://");

    public string Normalize()
    {
        var s = Server.Trim().TrimEnd('/');
        return s.EndsWith("/api/favs") ? s[..^"/api/favs".Length] : s;
    }
}

/// <summary>收藏云同步客户端（对接 Web 端 api/favs.php，多端 union 合并）</summary>
public static class FavSync
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static SyncConfig LoadConfig(string dir)
    {
        try
        {
            var path = Path.Combine(dir, "sync.json");
            return File.Exists(path)
                ? JsonSerializer.Deserialize<SyncConfig>(File.ReadAllText(path)) ?? new SyncConfig()
                : new SyncConfig();
        }
        catch { return new SyncConfig(); }
    }

    public static void SaveConfig(string dir, SyncConfig cfg)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "sync.json"),
            JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>拉取云端收藏并返回</summary>
    public static async Task<List<string>> PullAsync(SyncConfig cfg, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(
            $"{cfg.Normalize()}/api/favs?code={Uri.EscapeDataString(cfg.Code)}", ct));
        return Parse(doc.RootElement);
    }

    /// <summary>把本地收藏推到云端（服务器 union 合并），返回合并后的完整列表</summary>
    public static async Task<List<string>> PushMergeAsync(
        SyncConfig cfg, IEnumerable<string> localIds, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { code = cfg.Code, ids = localIds });
        var resp = await Http.PostAsync(
            $"{cfg.Normalize()}/api/favs",
            new StringContent(payload, Encoding.UTF8, "application/json"), ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return Parse(doc.RootElement);
    }

    /// <summary>完整同步：推送本地合并结果，再用结果覆盖本地</summary>
    public static async Task<int> SyncNowAsync(SyncConfig cfg, FavStore store, CancellationToken ct = default)
    {
        var merged = await PushMergeAsync(cfg, store.Ids, ct);
        store.Replace(merged);
        return merged.Count;
    }

    private static List<string> Parse(JsonElement root)
    {
        if (!root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True ||
            !root.TryGetProperty("favs", out var favs) || favs.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("同步服务返回格式异常");
        }
        return favs.EnumerateArray()
            .Select(x => x.GetString() ?? "")
            .Where(s => s != "")
            .Distinct()
            .ToList();
    }
}
