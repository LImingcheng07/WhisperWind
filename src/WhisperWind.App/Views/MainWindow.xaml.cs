using System;
using System.IO;
using System.Windows;
using WhisperWind.App.Services;
using WhisperWind.App.ViewModels;

namespace WhisperWind.App.Views;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();

        // 简易 DI
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var player = new WindowsNotePlayer();
        var watcher = new TargetWindowWatcher();
        var engine = new HarmonicaEngine(player, watcher);
        var hotkey = new GlobalHotkeyService();
        _vm = new MainViewModel(engine, watcher, hotkey, appData);

        DataContext = _vm;
        Loaded += (_, _) => _vm.StartServices(this);
        Closed += async (_, _) =>
        {
            _vm.StopServices();
            await engine.DisposeAsync();
            watcher.Dispose();
            hotkey.Dispose();
        };
    }
}
