using System;
using System.Windows;
using WhisperWind.App.Online;
using WhisperWind.App.Services;
using WhisperWind.App.ViewModels;

namespace WhisperWind.App.Views;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();

        // === DI 容器（极简）===
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var settings = AppSettings.Load(appData);
        var watcher = new TargetWindowWatcher();
        var engine = new HarmonicaEngine(new WindowsNotePlayer(), watcher);
        var hotkey = new GlobalHotkeyService();
        var importer = new MidiImportService(appData);
        var online = new OnlineLibraryService(appData);
        var ai = new AiAdvisorService();

        var vm = new MainViewModel(engine, watcher, hotkey, importer, online, ai, settings, appData);

        // 全局静态通道，让所有 Page 都能拿
        WhisperWind.App.App.MainVM = vm;
        DataContext = vm;

        Loaded += (_, _) =>
        {
            RootNav.Navigate(typeof(NowPlayingView));
            vm.StartServices(this);
        };
        Closed += (_, _) => vm.StopServices();
    }
}
