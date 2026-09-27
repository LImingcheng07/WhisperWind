using System;
using System.Windows;
using System.Windows.Controls;
using WhisperWind.App;
using WhisperWind.App.Services;

namespace WhisperWind.App.Views;

public partial class SettingsView : Page
{
    private AppSettings _settings = new();

    public SettingsView() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _settings = AppSettings.Load(appData);

        // Provider
        switch (_settings.AiProvider)
        {
            case "openai":
                ProviderCombo.SelectedIndex = 1;
                break;
            case "custom":
                ProviderCombo.SelectedIndex = 2;
                break;
            default:
                ProviderCombo.SelectedIndex = 0;
                break;
        }

        AnthropicKeyBox.Password = _settings.AnthropicApiKey ?? "";
        OpenAIKeyBox.Password = _settings.OpenaiApiKey ?? "";
        CustomKeyBox.Password = _settings.CustomApiKey ?? "";
        CustomEndpointBox.Text = _settings.CustomEndpoint;
        ModelBox.Text = _settings.AiModel;

        switch (_settings.OnlineProvider)
        {
            case "midisss": OnlineProviderCombo.SelectedIndex = 1; break;
            case "custom": OnlineProviderCombo.SelectedIndex = 2; break;
            default: OnlineProviderCombo.SelectedIndex = 0; break;
        }
        CustomIndexBox.Text = _settings.CustomIndexUrl ?? "";
        UpdateProviderPanels();
    }

    private void OnProviderChanged(object sender, SelectionChangedEventArgs e) => UpdateProviderPanels();

    private void OnOnlineProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CustomIndexPanel == null) return;
        var tag = (OnlineProviderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        CustomIndexPanel.Visibility = tag == "custom" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateProviderPanels()
    {
        if (AnthropicPanel == null || ProviderCombo == null) return;
        var tag = (ProviderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        AnthropicPanel.Visibility = tag == "anthropic" ? Visibility.Visible : Visibility.Collapsed;
        OpenAIPanel.Visibility = tag == "openai" ? Visibility.Visible : Visibility.Collapsed;
        CustomPanel.Visibility = tag == "custom" ? Visibility.Visible : Visibility.Collapsed;

        // 默认模型提示
        if (ModelBox.Text == AiAdvisorService.AnthropicModel || string.IsNullOrWhiteSpace(ModelBox.Text))
        {
            ModelBox.Text = tag switch
            {
                "anthropic" => AiAdvisorService.AnthropicModel,
                "openai" => AiAdvisorService.OpenAIModel,
                "custom" => "gpt-4o-mini",
                _ => AiAdvisorService.AnthropicModel,
            };
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var tag = (ProviderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "anthropic";
        _settings.AiProvider = tag;
        _settings.AnthropicApiKey = AnthropicKeyBox.Password;
        _settings.OpenaiApiKey = OpenAIKeyBox.Password;
        _settings.CustomApiKey = CustomKeyBox.Password;
        _settings.CustomEndpoint = string.IsNullOrWhiteSpace(CustomEndpointBox.Text)
            ? "https://api.openai.com/v1/chat/completions"
            : CustomEndpointBox.Text;
        _settings.AiModel = ModelBox.Text;

        var onlineTag = (OnlineProviderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "bitmidi";
        _settings.OnlineProvider = onlineTag;
        _settings.CustomIndexUrl = onlineTag == "custom" ? CustomIndexBox.Text : null;

        try
        {
            _settings.Save();
            // 通知 VM
            App.MainVM?.ReloadSettings(_settings);
            StatusText.Text = "✅ 已保存。重启应用后 AI 设置生效。";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ 保存失败: {ex.Message}";
        }
    }
}
