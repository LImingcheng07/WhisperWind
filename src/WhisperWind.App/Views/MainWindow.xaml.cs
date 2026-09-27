using System.Windows;
using Wpf.Ui.Controls;

namespace WhisperWind.App.Views;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        RootNav.Navigated += (_, e) =>
        {
            ContentFrame.Navigate(e.SourcePageType);
        };
        // 默认进入"此刻吹"
        ContentFrame.Navigate(typeof(NowPlayingView));
    }
}
