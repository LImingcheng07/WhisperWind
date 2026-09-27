using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace WhisperWind.App.Views;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        // NavigationView 内部用 Frame="{Binding ElementName=ContentFrame}" 自动 navigate
        // 加载完后手动跳到首页
        Loaded += (_, _) => ContentFrame.Navigate(typeof(NowPlayingView));
    }
}
