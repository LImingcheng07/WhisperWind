using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WhisperWind.App.Services;

public sealed class AppSettings
{
    [JsonPropertyName("aiProvider")] public string AiProvider { get; set; } = "anthropic";
    [JsonPropertyName("anthropicApiKey")] public string? AnthropicApiKey { get; set; }
    [JsonPropertyName("openaiApiKey")] public string? OpenaiApiKey { get; set; }
    [JsonPropertyName("customApiKey")] public string? CustomApiKey { get; set; }
    [JsonPropertyName("aiModel")] public string AiModel { get; set; } = AiAdvisorService.AnthropicModel;
    [JsonPropertyName("customEndpoint")] public string CustomEndpoint { get; set; } = "https://api.openai.com/v1/chat/completions";
    [JsonPropertyName("onlineProvider")] public string OnlineProvider { get; set; } = "bitmidi";
    [JsonPropertyName("customIndexUrl")] public string? CustomIndexUrl { get; set; }

    private static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WhisperWind", "settings.json");

    public static AppSettings Load(string? appDataDir = null)
    {
        var path = appDataDir is null
            ? DefaultPath
            : Path.Combine(appDataDir, "WhisperWind", "settings.json");
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

    public void Save(string? appDataDir = null)
    {
        var path = appDataDir is null
            ? DefaultPath
            : Path.Combine(appDataDir, "WhisperWind", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
