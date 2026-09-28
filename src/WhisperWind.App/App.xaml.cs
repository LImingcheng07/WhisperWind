using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using WhisperWind.App.BuiltIn;
using WhisperWind.App.ViewModels;
using WhisperWind.App.Views;

namespace WhisperWind.App;

public partial class App : Application
{
    public static MainViewModel? MainVM { get; private set; }

    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint ms);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;

        // 1ms 计时精度：按键节奏靠 Sleep/Delay，默认 15.6ms 太粗
        timeBeginPeriod(1);

        try
        {
            Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(
                Color.FromRgb(0xC0, 0x46, 0x3A), Wpf.Ui.Appearance.ApplicationTheme.Light);
        }
        catch
        {
            // 主题色失败不影响使用
        }

        Directory.CreateDirectory(MainViewModel.DataDir);
        try
        {
            BuiltInTracks.EnsureBuilt(MainViewModel.DataDir);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"生成内置曲目失败：{ex.Message}", "风声未止", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        MainVM = new MainViewModel();
        var win = new MainWindow { DataContext = MainVM };
        MainWindow = win;
        win.Show();
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        if (MainVM != null) MainVM.Toast($"出错了：{e.Exception.Message}");
        else MessageBox.Show(e.Exception.ToString(), "风声未止", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            MainVM?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
        timeEndPeriod(1);
        base.OnExit(e);
    }
}
