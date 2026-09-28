using System.Windows.Controls;
using System.Windows.Input;
using WhisperWind.App.ViewModels;

namespace WhisperWind.App.Views.Pages;

public partial class NowPlayingPage : UserControl
{
    public NowPlayingPage()
    {
        InitializeComponent();
        Harp.HoleClicked += hole => (DataContext as MainViewModel)?.PreviewHole(hole);
    }

    private void OnSeekStart(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.IsSeeking = true;
    }

    private void OnSeekEnd(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        vm.IsSeeking = false;
        vm.SeekTo(Progress.Value);
    }
}
