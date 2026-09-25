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

/// <summary>云同步配置（同步码；服务器地址有默认值，一般无需填写）</summary>
public sealed class SyncConfig
{
    public const string DefaultServer = "https://xingclouddisk.share.zrok.io/weather-alert-web";

    public string Server { get; set; } = "";
    public string Code { get; set; } = "";

    public bool Enabled => Code.Trim().Length >= 4; // 服务器有默认值，只看同步码

    public string Normalize()
    {
        var s = Server.Trim();
        if (s == "") s = DefaultServer;
        s = s.TrimEnd('/');
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

/// <summary>账号会话（token 鉴权）</summary>
public sealed class AccountSession
{
    public string Server { get; set; } = "";
    public string Token { get; set; } = "";
    public string User { get; set; } = "";
    public bool Valid => Token != "";
}

public static class AccountSync
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static string Base(string server) =>
        (server.Trim() == "" ? SyncConfig.DefaultServer : server).Trim().TrimEnd('/');

    public static string ConfigPath(string dir) => Path.Combine(dir, "account.json");

    public static AccountSession Load(string dir) =>
        File.Exists(ConfigPath(dir))
            ? JsonSerializer.Deserialize<AccountSession>(File.ReadAllText(ConfigPath(dir))) ?? new AccountSession()
            : new AccountSession();

    public static void Save(string dir, AccountSession s)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(ConfigPath(dir), JsonSerializer.Serialize(s));
    }

    private static async Task<JsonElement> PostAsync(string url, Dictionary<string, string> form, CancellationToken ct)
    {
        var resp = await Http.PostAsync(url, new FormUrlEncodedContent(form), ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        if (!root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
        {
            var msg = root.TryGetProperty("error", out var e) ? e.GetString() : null;
            throw new InvalidOperationException(msg ?? "接口返回异常");
        }
        return root.Clone();
    }

    private static AccountSession FromJson(string server, JsonElement root)
    {
        return new AccountSession
        {
            Server = server,
            Token = root.TryGetProperty("token", out var t) ? t.GetString() ?? "" : "",
            User = root.TryGetProperty("user", out var u) ? u.GetString() ?? "" : "",
        };
    }

    public static Task<AccountSession> SignupAsync(string server, string type, string name, string password, string email, string authcode, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["action"] = "signup", ["type"] = type,
        };
        if (type == "emailauthcode")
        {
            form["email"] = email;
            form["authcode"] = authcode;
        }
        else
        {
            form["name"] = name;
            form["password"] = password;
        }
        return PostThenSessionAsync(server, form, ct);
    }

    public static Task<AccountSession> LoginAsync(string server, string name, string password, string email, string authcode, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["action"] = "login" };
        if (email != "" && authcode != "")
        {
            form["email"] = email;
            form["authcode"] = authcode;
        }
        else
        {
            form["name"] = name;
            form["password"] = password;
        }
        return PostThenSessionAsync(server, form, ct);
    }

    public static async Task SendAuthCodeAsync(string server, string email, CancellationToken ct = default)
    {
        await PostAsync(Base(server) + "/api/account.php", new Dictionary<string, string>
        {
            ["action"] = "sendcode", ["email"] = email,
        }, ct);
    }

    private static async Task<AccountSession> PostThenSessionAsync(string server, Dictionary<string, string> form, CancellationToken ct)
    {
        var root = await PostAsync(Base(server) + "/api/account.php", form, ct);
        return FromJson(server, root);
    }

    /// <summary>账号收藏 union 合并：推送本地，返回合并结果</summary>
    public static async Task<List<string>> PushMergeAsync(string server, AccountSession session, IEnumerable<string> localIds, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { token = session.Token, ids = localIds });
        var resp = await Http.PostAsync(Base(server) + "/api/userfavs",
            new StringContent(payload, Encoding.UTF8, "application/json"), ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return Parse(doc.RootElement);
    }

    /// <summary>完整账号同步：推送本地合并，再覆盖本地</summary>
    public static async Task<int> SyncNowAsync(string server, AccountSession session, FavStore store, CancellationToken ct = default)
    {
        var merged = await PushMergeAsync(server, session, store.Ids, ct);
        store.Replace(merged);
        return merged.Count;
    }
}
