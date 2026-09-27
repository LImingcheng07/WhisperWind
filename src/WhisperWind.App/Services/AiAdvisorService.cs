using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using WhisperWind.Core;

namespace WhisperWind.App.Services;

/// <summary>
/// AI 知音：支持 Anthropic Claude / OpenAI 兼容（含自建 / 中转 / Ollama / OpenRouter / 国产）/ 自定义 HTTP 端点。
/// 默认关闭（用户在 Settings 填 API Key 启用）。密钥只存本地。
/// </summary>
public sealed class AiAdvisorService
{
    public enum Provider
    {
        Anthropic,
        OpenAI,
        Custom,
    }

    // 默认模型 / 端点
    public const string AnthropicModel = "claude-haiku-4-5-20251001";
    public const string AnthropicEndpoint = "https://api.anthropic.com/v1/messages";
    public const string OpenAIModel = "gpt-4o-mini";
    public const string OpenAIEndpoint = "https://api.openai.com/v1/chat/completions";

    public Provider CurrentProvider { get; set; } = Provider.Anthropic;
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = AnthropicModel;
    public string Endpoint { get; set; } = AnthropicEndpoint;
    public int MaxTokens { get; set; } = 1024;

    public bool IsEnabled => !string.IsNullOrWhiteSpace(ApiKey);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly List<ChatTurn> _history = new();

    public sealed record ChatTurn(string Role, string Content);

    public async Task<string> AskAsync(string userMessage, PlaybackPlan? plan, IReadOnlyList<string> recentWarnings)
    {
        if (!IsEnabled)
            return "🔕 AI 知音未启用。请在 Settings 填 API Key 后重启。\n（密钥只存本地，不上传）";

        var system = BuildSystemPrompt(plan, recentWarnings);
        return CurrentProvider switch
        {
            Provider.Anthropic => await AskAnthropicAsync(system, userMessage),
            Provider.OpenAI or Provider.Custom => await AskOpenAIAsync(system, userMessage),
            _ => "未知 provider",
        };
    }

    private async Task<string> AskAnthropicAsync(string system, string userMessage)
    {
        var reqBody = new
        {
            model = Model,
            max_tokens = MaxTokens,
            messages = new object[] { new { role = "user", content = system + "\n\n" + userMessage } },
        };
        var json = JsonSerializer.Serialize(reqBody);
        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("x-api-key", ApiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");

        using var resp = await _http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Anthropic {(int)resp.StatusCode}: {body[..Math.Min(400, body.Length)]}");

        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "(empty)";
        _history.Add(new ChatTurn("user", userMessage));
        _history.Add(new ChatTurn("assistant", text));
        return text;
    }

    private async Task<string> AskOpenAIAsync(string system, string userMessage)
    {
        var messages = new List<object>
        {
            new { role = "system", content = system },
            new { role = "user", content = userMessage },
        };
        // 注入历史
        foreach (var h in _history)
            messages.Add(new { role = h.Role, content = h.Content });

        var reqBody = new
        {
            model = Model,
            max_tokens = MaxTokens,
            messages,
            temperature = 0.7,
        };
        var json = JsonSerializer.Serialize(reqBody);
        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);

        using var resp = await _http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenAI {(int)resp.StatusCode}: {body[..Math.Min(400, body.Length)]}");

        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "(empty)";
        _history.Add(new ChatTurn("user", userMessage));
        _history.Add(new ChatTurn("assistant", text));
        return text;
    }

    private static string BuildSystemPrompt(PlaybackPlan? plan, IReadOnlyList<string> recentWarnings)
    {
        var sb = new StringBuilder();
        sb.AppendLine("你是「风声未止」三角洲行动自动口琴的 AI 助手「知音」。");
        sb.AppendLine("用户是国内口琴玩家，正在用这个开源工具自动演奏。");
        sb.AppendLine("回答要简洁、口语化、能直接拿来用。不要 markdown 标题。");
        sb.AppendLine();
        if (plan is not null)
        {
            sb.AppendLine($"当前曲目: {Path.GetFileName(plan.SongPath)}");
            sb.AppendLine($"  原 {plan.OriginalNoteCount} 音符 → 转 {plan.ConvertedNoteCount} 键事件，丢 {plan.DroppedNoteCount}");
            sb.AppendLine($"  时长: {plan.DurationMs / 1000.0:F1}s");
            if (plan.Warnings.Count > 0)
            {
                sb.AppendLine($"  警告 (前 5):");
                foreach (var w in plan.Warnings.Take(5))
                    sb.AppendLine($"    - {w}");
            }
            if (plan.Errors.Count > 0)
            {
                sb.AppendLine($"  错误 (前 5):");
                foreach (var e in plan.Errors.Take(5))
                    sb.AppendLine($"    - {e}");
            }
        }
        else
        {
            sb.AppendLine("当前未加载曲目。");
        }
        if (recentWarnings.Count > 0)
        {
            sb.AppendLine("最近演奏警告:");
            foreach (var w in recentWarnings.Take(3))
                sb.AppendLine($"  - {w}");
        }
        return sb.ToString();
    }

    public void ClearHistory() => _history.Clear();
}
