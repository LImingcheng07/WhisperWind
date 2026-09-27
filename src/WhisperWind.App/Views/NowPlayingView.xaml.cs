using System.Windows;
using System.Windows.Controls;

namespace WhisperWind.App.Views;

public partial class NowPlayingView : Page
{
    public NowPlayingView()
    {
        InitializeComponent();
    }

    private void PlayBtn_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "演奏中…（占位）";
        PlayBtn.Content = "⏸ 暂停";
    }
}
