using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace WhisperWind.App.Online;

/// <summary>
/// 在线曲库：访问一个轻量 JSON 索引 + 直链 .mid。
/// 索引格式（每行一首）：
/// {
///   "name": "千本桜",
///   "author": "黒うさP",
///   "bpm": 200,
///   "url": "https://example.com/senbonzakura.mid",
///   "sha256": "abc..."  // 可选，留空则下载后计算
/// }
/// 默认索引：用户可在 Settings 自定义 URL；首次启动用内置兜底（github raw 即可）。
/// </summary>
public sealed class OnlineTrackMeta
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("author")] public string Author { get; set; } = "";
    [JsonPropertyName("bpm")] public int Bpm { get; set; }
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
}

public sealed class OnlineLibraryService : IDisposable
{
    private const string DefaultIndexUrl =
        "https://raw.githubusercontent.com/LImingcheng07/WhisperWind-tracks/main/index.json";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly string _cacheDir;

    public OnlineLibraryService(string appDataDir)
    {
        _cacheDir = Path.Combine(appDataDir, "WhisperWind", "online");
        Directory.CreateDirectory(_cacheDir);
    }

    public async Task<IReadOnlyList<OnlineTrackMeta>> FetchIndexAsync(string? customUrl = null)
    {
        var url = customUrl ?? DefaultIndexUrl;
        var json = await _http.GetStringAsync(url);
        return JsonSerializer.Deserialize<List<OnlineTrackMeta>>(json) ?? new();
    }

    /// <summary>下载到本地缓存，path 返回绝对路径</summary>
    public async Task<string> DownloadAsync(OnlineTrackMeta meta, IProgress<double>? progress = null)
    {
        var fileName = SanitizeFileName($"{meta.Name} - {meta.Author}.mid");
        var path = Path.Combine(_cacheDir, fileName);
        if (File.Exists(path)) return path;

        // 流式下载，进度报告
        using var resp = await _http.GetAsync(meta.Url, HttpCompletionOption.ResponseHeadersRead);
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

    public void Dispose() => _http.Dispose();

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
