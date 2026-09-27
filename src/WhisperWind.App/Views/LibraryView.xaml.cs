using System.Windows;
using System.Windows.Controls;
using WhisperWind.App;

namespace WhisperWind.App.Views;

public partial class LibraryView : Page
{
    public LibraryView() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (App.MainVM is not null) DataContext = App.MainVM;
    }
}
