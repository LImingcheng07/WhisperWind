using System.Windows;
using Wpf.Ui.Controls;

namespace WhisperWind.App.Views;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => RootNav.Navigate(typeof(NowPlayingView));
    }
}
