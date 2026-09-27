using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace WhisperWind.App.Online;

/// <summary>
/// 在线曲库：默认走 BitMidi.com（无需 key，无需注册，曲库 10000+ 流行/动漫/影视/古典）。
///   - 列表: GET /?page=N  (N 从 0 开始，每页 15 首)
///   - 详情: GET /<slug>-mid/  含 .mid 直链
///   - 下载: GET /uploads/{id}.mid
/// 备选: 用户在 Settings 自定义 JSON 索引 URL（兼容 self-hosted / 其他 API）。
/// </summary>
public sealed class OnlineTrackMeta
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("author")] public string Author { get; set; } = "";
    [JsonPropertyName("bpm")] public int Bpm { get; set; }
    [JsonPropertyName("notes")] public string Notes { get; set; } = "";
    [JsonPropertyName("average_rating")] public double Rating { get; set; }
    /// <summary>若为空走 BitMidi /uploads/{id}.mid，否则用直链</summary>
    [JsonPropertyName("url")] public string? Url { get; set; }
}

public sealed class OnlineLibraryService : IDisposable
{
    /// <summary>默认源：BitMidi.com</summary>
    public const string DefaultProvider = "bitmidi";

    private const string BitmidiBase = "https://bitmidi.com";
    private const int PerPage = 15;

    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders = { { "User-Agent", "WhisperWind/1.0" } },
    };

    private readonly string _cacheDir;

    public string Provider { get; set; } = DefaultProvider;
    public string? CustomIndexUrl { get; set; }

    public OnlineLibraryService(string appDataDir)
    {
        _cacheDir = Path.Combine(appDataDir, "WhisperWind", "online");
        Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>按关键词搜索；query 为空时返回热门/最近</summary>
    public async Task<IReadOnlyList<OnlineTrackMeta>> SearchAsync(string query = "")
    {
        if (Provider == "custom" && !string.IsNullOrEmpty(CustomIndexUrl))
            return await FetchCustomIndexAsync();

        // BitMidi 搜索: q= 参数
        var url = $"{BitmidiBase}/?q={Uri.EscapeDataString(query)}";
        var html = await _http.GetStringAsync(url);
        return ParseBitmidiListHtml(html);
    }

    /// <summary>按页索引（0 起），每页 15 首</summary>
    public async Task<IReadOnlyList<OnlineTrackMeta>> GetPageAsync(int page)
    {
        if (Provider == "custom" && !string.IsNullOrEmpty(CustomIndexUrl))
            return await FetchCustomIndexAsync();

        var url = $"{BitmidiBase}/?page={page}";
        var html = await _http.GetStringAsync(url);
        return ParseBitmidiListHtml(html);
    }

    /// <summary>下载到本地缓存，返回绝对路径</summary>
    public async Task<string> DownloadAsync(OnlineTrackMeta meta, IProgress<double>? progress = null)
    {
        var url = meta.Url ?? $"{BitmidiBase}/uploads/{meta.Id}.mid";
        // 如果是详情页（含 -mid/），先解析出 .mid 直链
        if (url.EndsWith("-mid/") || url.EndsWith("-mid"))
        {
            url = await ResolveDownloadUrlAsync(url);
        }
        var fileName = SanitizeFileName($"{meta.Id} - {meta.Name}.mid");
        var path = Path.Combine(_cacheDir, fileName);
        if (File.Exists(path) && new FileInfo(path).Length > 0) return path;

        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1L;

        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var dst = File.Create(path);
        var buffer = new byte[8192];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n));
            read += n;
            if (total > 0) progress?.Report((double)read / total);
        }
        progress?.Report(1.0);
        return path;
    }

    private static IReadOnlyList<OnlineTrackMeta> ParseBitmidiListHtml(string html)
    {
        // 抓 slug 列表: <a href="/<slug>-mid/">
        var slugs = System.Text.RegularExpressions.Regex.Matches(
            html, @"href=""/([^/""-]+(?:-[^/""-]+)*?)-mid/?\?""");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<OnlineTrackMeta>();
        foreach (System.Text.RegularExpressions.Match m in slugs)
        {
            var slug = m.Groups[1].Value;
            if (!seen.Add(slug)) continue;
            var name = System.Globalization.CultureInfo.InvariantCulture.TextInfo
                .ToTitleCase(slug.Replace('-', ' '));
            // 列表页只拿 slug，Url 指向详情页，DownloadAsync 内部会再请求详情页拿直链
            list.Add(new OnlineTrackMeta
            {
                Id = Math.Abs(slug.GetHashCode()) & 0x7fffffff,
                Name = name,
                Author = "BitMidi",
                Url = $"{BitmidiBase}/{slug}-mid/",
            });
        }
        return list;
    }

    /// <summary>解析详情页拿到 .mid 直链</summary>
    private async Task<string> ResolveDownloadUrlAsync(string detailPageUrl)
    {
        var html = await _http.GetStringAsync(detailPageUrl);
        var m = System.Text.RegularExpressions.Regex.Match(
            html, @"href=""([^""]+\.mid)""");
        if (m.Success)
        {
            var u = m.Groups[1].Value;
            if (u.StartsWith("/")) u = BitmidiBase + u;
            return u;
        }
        throw new InvalidOperationException($"未找到 .mid 直链: {detailPageUrl}");
    }

    private async Task<IReadOnlyList<OnlineTrackMeta>> FetchCustomIndexAsync()
    {
        var json = await _http.GetStringAsync(CustomIndexUrl!);
        return JsonSerializer.Deserialize<List<OnlineTrackMeta>>(json) ?? new();
    }

    public void Dispose() => _http.Dispose();

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Length > 120 ? name[..120] : name;
    }
}
