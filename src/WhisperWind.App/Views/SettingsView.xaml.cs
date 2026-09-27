using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using WhisperWind.App;
using WhisperWind.App.Services;

namespace WhisperWind.App.Views;

public partial class SettingsView : Page
{
    public SettingsView() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var s = AppSettings.Load(appData);
        ApiKeyBox.Password = s.AnthropicApiKey ?? "";
        foreach (ComboBoxItem item in ModelCombo.Items)
            if ((string)item.Content == s.AiModel) { item.IsSelected = true; break; }
        IndexUrlBox.Text = s.OnlineIndexUrl
            ?? "https://raw.githubusercontent.com/LImingcheng07/WhisperWind-tracks/main/index.json";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var s = AppSettings.Load(appData);
            s.AnthropicApiKey = string.IsNullOrWhiteSpace(ApiKeyBox.Password) ? null : ApiKeyBox.Password;
            s.AiModel = (ModelCombo.SelectedItem is ComboBoxItem item) ? (string)item.Content : s.AiModel;
            s.OnlineIndexUrl = string.IsNullOrWhiteSpace(IndexUrlBox.Text) ? null : IndexUrlBox.Text;
            s.Save(appData);

            if (App.MainVM is not null)
            {
                // 重新加载
                var settings = AppSettings.Load(appData);
                App.MainVM.ReloadSettings(settings);
            }
            StatusText.Text = $"✓ 已保存于 {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ {ex.Message}";
        }
    }
}
