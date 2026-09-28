using System.Windows.Controls;
using System.Windows.Input;
using WhisperWind.App.ViewModels;

namespace WhisperWind.App.Views.Pages;

public partial class OnlinePage : UserControl
{
    public OnlinePage() => InitializeComponent();

    private void OnQueryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainViewModel vm)
        {
            vm.SearchOnlineCommand.Execute(null);
            e.Handled = true;
        }
    }
}
