using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace WhisperWind.App.Online;

/// <summary>一首在线曲目</summary>
public sealed class OnlineTrackMeta
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("author")] public string Author { get; set; } = "";
    [JsonPropertyName("plays")] public long Plays { get; set; }
    [JsonPropertyName("views")] public long Views { get; set; }
    /// <summary>.mid 直链（相对 BitMidi 或绝对地址）</summary>
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("downloadUrl")] public string? DownloadUrl { get; set; }

    /// <summary>去掉 .mid 后缀、下划线的展示名</summary>
    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            var n = Name;
            if (n.EndsWith(".mid", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
            else if (n.EndsWith(".midi", StringComparison.OrdinalIgnoreCase)) n = n[..^5];
            return n.Replace('_', ' ').Trim();
        }
    }

    [JsonIgnore]
    public string PlaysText => Plays >= 10000 ? $"{Plays / 10000.0:0.#} 万次播放" : $"{Plays} 次播放";

    [JsonIgnore] public bool IsDownloaded { get; set; }
}

public sealed record OnlinePage(IReadOnlyList<OnlineTrackMeta> Items, int Page, int PageTotal, long Total);

/// <summary>
/// 在线曲库：默认 BitMidi.com 公开 JSON 接口（无需 key）。
///   搜索: GET /api/midi/search?q=..&amp;page=N   （N 从 0 开始，每页 15 首）
///   热门: GET /api/midi/all?page=N              （按播放量排序）
///   下载: GET /uploads/{id}.mid
/// BitMidi 只能搜英文名/拼音。也支持在设置里填自定义 JSON 索引（OnlineTrackMeta 数组）。
/// </summary>
public sealed class OnlineLibraryService : IDisposable
{
    public const string DefaultProvider = "bitmidi";
    private const string BitmidiBase = "https://bitmidi.com";

    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
        DefaultRequestHeaders = { { "User-Agent", "WhisperWind/1.0 (+https://github.com/LImingcheng07/WhisperWind)" } },
    };

    private readonly string _cacheDir;
    private List<OnlineTrackMeta>? _customCache;

    public string Provider { get; set; } = DefaultProvider;
    public string? CustomIndexUrl { get; set; }
    public string CacheDir => _cacheDir;

    public OnlineLibraryService(string dataDir)
    {
        _cacheDir = Path.Combine(dataDir, "online");
        Directory.CreateDirectory(_cacheDir);
    }

    private bool UseCustom => Provider == "custom" && !string.IsNullOrWhiteSpace(CustomIndexUrl);

    /// <summary>搜索；query 为空时返回热门</summary>
    public async Task<OnlinePage> SearchAsync(string? query, int page, CancellationToken ct = default)
    {
        query = query?.Trim() ?? "";
        if (UseCustom) return await SearchCustomAsync(query, page, ct);

        var url = query.Length == 0
            ? $"{BitmidiBase}/api/midi/all?page={page}"
            : $"{BitmidiBase}/api/midi/search?q={Uri.EscapeDataString(query)}&page={page}";
        using var stream = await _http.GetStreamAsync(url, ct);
        var resp = await JsonSerializer.DeserializeAsync<BitmidiResponse>(stream, cancellationToken: ct);
        var r = resp?.Result;
        var items = r?.Results ?? new List<OnlineTrackMeta>();
        foreach (var i in items) i.IsDownloaded = File.Exists(CachePath(i));
        return new OnlinePage(items, page, Math.Max(r?.PageTotal ?? 1, 1), r?.Total ?? items.Count);
    }

    /// <summary>下载到本地缓存（已下载则直接返回），返回绝对路径</summary>
    public async Task<string> DownloadAsync(OnlineTrackMeta meta, CancellationToken ct = default)
    {
        var path = CachePath(meta);
        if (File.Exists(path) && new FileInfo(path).Length > 0) return path;

        var url = meta.DownloadUrl ?? meta.Url ?? $"/uploads/{meta.Id}.mid";
        if (url.StartsWith('/')) url = BitmidiBase + url;

        var bytes = await _http.GetByteArrayAsync(url, ct);
        // MIDI 文件必须以 MThd 开头，防止下到 HTML 错误页
        if (bytes.Length < 14 || bytes[0] != 'M' || bytes[1] != 'T' || bytes[2] != 'h' || bytes[3] != 'd')
            throw new InvalidDataException("下载到的不是有效的 MIDI 文件");

        var tmp = path + ".part";
        await File.WriteAllBytesAsync(tmp, bytes, ct);
        File.Move(tmp, path, overwrite: true);
        meta.IsDownloaded = true;
        return path;
    }

    public string CachePath(OnlineTrackMeta meta)
        => Path.Combine(_cacheDir, SanitizeFileName($"{meta.DisplayName} [{meta.Id}].mid"));

    private async Task<OnlinePage> SearchCustomAsync(string query, int page, CancellationToken ct)
    {
        if (_customCache == null)
        {
            var json = await _http.GetStringAsync(CustomIndexUrl!, ct);
            _customCache = JsonSerializer.Deserialize<List<OnlineTrackMeta>>(json) ?? new();
        }
        const int size = 15;
        var filtered = _customCache
            .Where(t => query.Length == 0
                        || t.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || t.Author.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var items = filtered.Skip(page * size).Take(size).ToList();
        foreach (var i in items) i.IsDownloaded = File.Exists(CachePath(i));
        return new OnlinePage(items, page, Math.Max(1, (filtered.Count + size - 1) / size), filtered.Count);
    }

    public void ResetCustomCache() => _customCache = null;

    public void Dispose() => _http.Dispose();

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Length > 120 ? name[..120] : name;
    }

    private sealed class BitmidiResponse
    {
        [JsonPropertyName("result")] public BitmidiResult? Result { get; set; }
    }

    private sealed class BitmidiResult
    {
        [JsonPropertyName("results")] public List<OnlineTrackMeta>? Results { get; set; }
        [JsonPropertyName("total")] public long Total { get; set; }
        [JsonPropertyName("pageTotal")] public int PageTotal { get; set; }
    }
}
