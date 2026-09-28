using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WhisperWind.App.Services;

/// <summary>每首歌单独记住的选轨/移调</summary>
public sealed class SongPrefs
{
    [JsonPropertyName("track")] public int? TrackIndex { get; set; }
    [JsonPropertyName("transpose")] public int? Transpose { get; set; }
}

public sealed class AppSettings
{
    // === AI ===
    [JsonPropertyName("aiProvider")] public string AiProvider { get; set; } = "anthropic";
    [JsonPropertyName("anthropicApiKey")] public string? AnthropicApiKey { get; set; }
    [JsonPropertyName("openaiApiKey")] public string? OpenaiApiKey { get; set; }
    [JsonPropertyName("customApiKey")] public string? CustomApiKey { get; set; }
    [JsonPropertyName("aiModel")] public string AiModel { get; set; } = AiAdvisorService.AnthropicModel;
    [JsonPropertyName("customEndpoint")] public string CustomEndpoint { get; set; } = "https://api.openai.com/v1/chat/completions";

    // === 在线曲库 ===
    [JsonPropertyName("onlineProvider")] public string OnlineProvider { get; set; } = "bitmidi";
    [JsonPropertyName("customIndexUrl")] public string? CustomIndexUrl { get; set; }

    // === 键位（WPF Key 名称，或 MouseLeft / MouseRight / MouseMiddle / MouseX1 / MouseX2）===
    /// <summary>键位默认值版本。v2 起按游戏实际键位：降调左键、半音中键、升调右键</summary>
    public const int CurrentKeysVersion = 2;
    [JsonPropertyName("keysVersion")] public int KeysVersion { get; set; } = CurrentKeysVersion;
    [JsonPropertyName("mainKeys")] public string[] MainKeys { get; set; } = DefaultMainKeys();
    [JsonPropertyName("sharpModifier")] public string SharpModifier { get; set; } = DefaultSharp;
    [JsonPropertyName("octaveModifier")] public string OctaveModifier { get; set; } = DefaultOctaveUp;
    [JsonPropertyName("octaveDownModifier")] public string OctaveDownModifier { get; set; } = DefaultOctaveDown;
    /// <summary>修饰键先按下多少毫秒再按主键，保证游戏先识别到修饰</summary>
    [JsonPropertyName("modifierLeadMs")] public int ModifierLeadMs { get; set; } = 10;

    // === 演奏 ===
    [JsonPropertyName("countdownSeconds")] public int CountdownSeconds { get; set; } = 3;
    [JsonPropertyName("minNoteGapMs")] public int MinNoteGapMs { get; set; } = 50;
    [JsonPropertyName("releaseGapMs")] public int ReleaseGapMs { get; set; } = 20;
    [JsonPropertyName("requireGameFocus")] public bool RequireGameFocus { get; set; } = true;
    [JsonPropertyName("gameWindowKeywords")] public string GameWindowKeywords { get; set; } = "三角洲行动;Delta Force;DeltaForce";
    [JsonPropertyName("speed")] public double Speed { get; set; } = 1.0;
    [JsonPropertyName("previewProgram")] public int PreviewProgram { get; set; } = 22; // GM Harmonica

    // === 悬浮窗 ===
    [JsonPropertyName("overlayAutoShow")] public bool OverlayAutoShow { get; set; } = true;
    [JsonPropertyName("overlayOpacity")] public double OverlayOpacity { get; set; } = 0.94;
    [JsonPropertyName("overlayLeft")] public double? OverlayLeft { get; set; }
    [JsonPropertyName("overlayTop")] public double? OverlayTop { get; set; }

    [JsonPropertyName("songs")] public Dictionary<string, SongPrefs> Songs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public const string DefaultSharp = "MouseMiddle";
    public const string DefaultOctaveUp = "MouseRight";
    public const string DefaultOctaveDown = "MouseLeft";

    public void ResetModifiers()
    {
        SharpModifier = DefaultSharp;
        OctaveModifier = DefaultOctaveUp;
        OctaveDownModifier = DefaultOctaveDown;
    }

    public static string[] DefaultMainKeys() => new[] { "Z", "X", "C", "V", "B", "N", "M", "OemComma" };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WhisperWind", "settings.json");

    public static AppSettings Load(string? appDataDir = null)
    {
        var path = PathFor(appDataDir);
        if (!File.Exists(path)) return new AppSettings();
        try
        {
            var json = File.ReadAllText(path);
            var s = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            // 旧版（没有 keysVersion 字段）默认的修饰键是错的（侧键半音 / 中键高八度），迁移到游戏实际键位
            if (!json.Contains("\"keysVersion\"") || s.KeysVersion < CurrentKeysVersion)
            {
                s.ResetModifiers();
                s.KeysVersion = CurrentKeysVersion;
            }
            if (s.MainKeys is not { Length: 8 }) s.MainKeys = DefaultMainKeys();
            s.Songs = new Dictionary<string, SongPrefs>(s.Songs ?? new(), StringComparer.OrdinalIgnoreCase);
            return s;
        }
        catch
        {
            return new AppSettings();
        }
    }

    /// <summary>原子写：先写临时文件再替换，避免写一半断电导致配置损坏</summary>
    public void Save(string? appDataDir = null)
    {
        var path = PathFor(appDataDir);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    private static string PathFor(string? appDataDir) => appDataDir is null
        ? DefaultPath
        : Path.Combine(appDataDir, "WhisperWind", "settings.json");
}
