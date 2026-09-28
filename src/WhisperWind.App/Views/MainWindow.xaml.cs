using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using WhisperWind.App.ViewModels;

namespace WhisperWind.App.Views;

public partial class MainWindow : Window
{
    private MiniPlayerWindow? _overlay;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        StateChanged += (_, _) => UpdateMaximizeState();
        Closing += OnClosing;
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is MainViewModel old) old.PropertyChanged -= OnVmPropertyChanged;
            if (e.NewValue is MainViewModel vm) vm.PropertyChanged += OnVmPropertyChanged;
        };
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private async void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        // Win11 圆角
        int round = 2;
        DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
        if (Vm != null) await Vm.StartAsync(hwnd);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.OverlayVisible) || Vm == null) return;
        if (Vm.OverlayVisible)
        {
            _overlay ??= new MiniPlayerWindow { DataContext = Vm };
            _overlay.Show();
        }
        else
        {
            _overlay?.Hide();
        }
    }

    private void UpdateMaximizeState()
    {
        // WindowChrome 最大化时窗口会超出屏幕边缘，补一圈边距
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_overlay != null)
        {
            _overlay.SavePosition();
            _overlay.ForceClose();
        }
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnCheckChipClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm != null) Vm.CurrentPage = "check";
    }

    private void OnToastClick(object sender, MouseButtonEventArgs e) => Vm?.DismissToastCommand.Execute(null);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
