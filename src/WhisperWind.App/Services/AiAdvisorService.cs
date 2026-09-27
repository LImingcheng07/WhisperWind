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
/// AI 知音：通过 Anthropic Claude API 给演奏建议。
/// 默认关闭（需要用户在 Settings 输入 API key）。
/// 给的输入：当前曲目 + 演奏历史 + 用户消息；
/// 输出：自然语言建议 + 可选动作（暂不支持 action，只给文）。
/// </summary>
public sealed class AiAdvisorService
{
    public const string DefaultModel = "claude-haiku-4-5-20251001";
    public const string DefaultEndpoint = "https://api.anthropic.com/v1/messages";

    public bool IsEnabled => !string.IsNullOrWhiteSpace(ApiKey);

    public string ApiKey { get; set; } = "";   // 由 Settings 写入
    public string Model { get; set; } = DefaultModel;
    public string Endpoint { get; set; } = DefaultEndpoint;
    public int MaxTokens { get; set; } = 1024;

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly List<ChatTurn> _history = new();

    public sealed record ChatTurn(string Role, string Content);

    public async Task<string> AskAsync(string userMessage, PlaybackPlan? plan, IReadOnlyList<string> recentWarnings)
    {
        if (!IsEnabled)
            return "🔕 AI 知音未启用。请在 Settings 里填入 Anthropic API Key 后重启。\n（密钥只存本地，不上传）";

        var system = BuildSystemPrompt(plan, recentWarnings);
        var messages = new List<object>
        {
            new { role = "user", content = system + "\n\n" + userMessage },
        };

        var reqBody = new
        {
            model = Model,
            max_tokens = MaxTokens,
            messages,
        };
        var json = JsonSerializer.Serialize(reqBody);
        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("x-api-key", ApiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Headers.Add("anthropic-beta", "prompt-caching-2024-07-31");

        using var resp = await _http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Claude API {(int)resp.StatusCode}: {body[..Math.Min(400, body.Length)]}");

        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString()
            ?? "(empty)";
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
