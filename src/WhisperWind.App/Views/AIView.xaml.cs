using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WhisperWind.App;

namespace WhisperWind.App.Views;

public partial class AIView : Page
{
    public AIView() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (App.MainVM is not null) DataContext = App.MainVM;
    }

    private void OnInputKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && App.MainVM is not null)
        {
            App.MainVM.AskAiCommand.Execute(null);
            e.Handled = true;
        }
    }
}
