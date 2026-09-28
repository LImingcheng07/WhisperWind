using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WhisperWind.App.ViewModels;

namespace WhisperWind.App.Views.Pages;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseDown += OnPreviewMouseDown;
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void OnSlotClick(object sender, RoutedEventArgs e) => Focus();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is not { IsCapturingKey: true } vm) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.None or Key.ImeProcessed) return;
        if (vm.CaptureKey(key.ToString())) e.Handled = true;
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Vm is not { IsCapturingKey: true } vm) return;
        string? id = e.ChangedButton switch
        {
            MouseButton.XButton1 => "MouseX1",
            MouseButton.XButton2 => "MouseX2",
            MouseButton.Middle => "MouseMiddle",
            MouseButton.Right => "MouseRight",
            _ => null,
        };
        if (id != null && vm.CaptureKey(id)) e.Handled = true;
    }
}
