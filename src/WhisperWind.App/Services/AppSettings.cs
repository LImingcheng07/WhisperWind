using System;
using System.IO;
using System.Text.Json;

namespace WhisperWind.App.Services;

/// <summary>
/// 用户配置持久化（%AppData%/WhisperWind/settings.json）。
/// 当前支持：Anthropic API key / AI Model / 在线曲库索引 URL / 自定义主键。
/// 不上传任何字段。
/// </summary>
public sealed class AppSettings
{
    public string? AnthropicApiKey { get; set; }
    public string AiModel { get; set; } = AiAdvisorService.DefaultModel;
    public string? OnlineIndexUrl { get; set; }
    public string? KeyMappingOverride { get; set; }  // "F1,F2,F3,F4,F5,F6,F7,F8"

    private static string SettingsPath(string appDataDir) =>
        Path.Combine(appDataDir, "WhisperWind", "settings.json");

    public static AppSettings Load(string appDataDir)
    {
        var path = SettingsPath(appDataDir);
        if (!File.Exists(path)) return new AppSettings();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(string appDataDir)
    {
        Directory.CreateDirectory(Path.Combine(appDataDir, "WhisperWind"));
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath(appDataDir), json);
    }
}
