using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperWind.App.Services;

namespace WhisperWind.App.ViewModels;

public sealed record ChatMessage(string Text, bool IsUser);

public partial class MainViewModel
{
    public ObservableCollection<ChatMessage> ChatMessages { get; } = new();

    [ObservableProperty] private string _aiInput = "";
    [ObservableProperty] private bool _aiBusy;
    [ObservableProperty] private bool _aiEnabled;

    private void InitAi()
    {
        ApplyAiSettings();
        ChatMessages.Add(new ChatMessage(
            "我是 AI 知音。可以问我：这首歌为什么很多音被折叠了？该选哪条轨？移调多少合适？\n" +
            "（需要先在「设置」里填 API Key，密钥只存在本机）", false));
    }

    private void ApplyAiSettings()
    {
        var s = _settings;
        _ai.CurrentProvider = s.AiProvider switch
        {
            "openai" => AiAdvisorService.Provider.OpenAI,
            "custom" => AiAdvisorService.Provider.Custom,
            _ => AiAdvisorService.Provider.Anthropic,
        };
        _ai.ApiKey = s.AiProvider switch
        {
            "openai" => s.OpenaiApiKey ?? "",
            "custom" => s.CustomApiKey ?? "",
            _ => s.AnthropicApiKey ?? "",
        };
        _ai.Model = string.IsNullOrWhiteSpace(s.AiModel)
            ? (_ai.CurrentProvider == AiAdvisorService.Provider.Anthropic ? AiAdvisorService.AnthropicModel : AiAdvisorService.OpenAIModel)
            : s.AiModel;
        _ai.Endpoint = _ai.CurrentProvider switch
        {
            AiAdvisorService.Provider.Custom when !string.IsNullOrWhiteSpace(s.CustomEndpoint) => s.CustomEndpoint,
            AiAdvisorService.Provider.Anthropic => AiAdvisorService.AnthropicEndpoint,
            _ => AiAdvisorService.OpenAIEndpoint,
        };
        AiEnabled = _ai.IsEnabled;
    }

    [RelayCommand]
    private async Task AskAiAsync()
    {
        var input = AiInput.Trim();
        if (input.Length == 0 || AiBusy) return;
        AiInput = "";
        ChatMessages.Add(new ChatMessage(input, true));
        AiBusy = true;
        try
        {
            var plan = _engine.Plan;
            var reply = await _ai.AskAsync(input, plan, plan?.Warnings.ToList() ?? new());
            ChatMessages.Add(new ChatMessage(reply, false));
        }
        catch (Exception ex)
        {
            ChatMessages.Add(new ChatMessage($"出错了：{ex.Message}", false));
        }
        finally
        {
            AiBusy = false;
        }
    }

    [RelayCommand]
    private void ClearAi()
    {
        _ai.ClearHistory();
        ChatMessages.Clear();
        InitAi();
    }
}
