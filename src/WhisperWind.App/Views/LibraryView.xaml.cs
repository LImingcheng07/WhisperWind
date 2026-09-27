using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WhisperWind.App;
using WhisperWind.App.BuiltIn;
using WhisperWind.App.Online;

namespace WhisperWind.App.Views;

public partial class LibraryView : Page
{
    public LibraryView() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (App.MainVM is not null)
        {
            DataContext = App.MainVM;
            LocalList.ItemsSource = App.MainVM.Tracks;
            OnlineList.ItemsSource = App.MainVM.OnlineTracks;
            App.MainVM.PropertyChanged += (_, ev) =>
            {
                if (ev.PropertyName == nameof(App.MainVM.OnlineStatus))
                    StatusText.Text = App.MainVM.OnlineStatus;
            };
            StatusText.Text = App.MainVM.OnlineStatus;
        }
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        if (App.MainVM is not null && App.MainVM.ImportLocalMidiCommand.CanExecute(null))
            App.MainVM.ImportLocalMidiCommand.Execute(null);
    }

    private void OnSearchClick(object sender, RoutedEventArgs e)
    {
        if (App.MainVM is null) return;
        App.MainVM.OnlineQuery = QueryBox.Text ?? "";
        if (App.MainVM.RefreshOnlineCommand.CanExecute(null))
            App.MainVM.RefreshOnlineCommand.Execute(null);
    }

    private void OnQueryKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnSearchClick(sender, new RoutedEventArgs());
    }

    private int _page = 0;
    private void OnPageClick(object sender, RoutedEventArgs e)
    {
        if (App.MainVM is null) return;
        _page++;
        if (App.MainVM.LoadOnlinePageCommand.CanExecute(_page))
            App.MainVM.LoadOnlinePageCommand.Execute(_page);
    }

    private void OnLocalSelect(object sender, SelectionChangedEventArgs e)
    {
        if (LocalList.SelectedItem is TrackMeta m && App.MainVM is not null)
        {
            if (App.MainVM.LoadTrackCommand.CanExecute(m))
                App.MainVM.LoadTrackCommand.Execute(m);
        }
    }

    private void OnDownloadOnlineClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button b && b.Tag is OnlineTrackMeta meta && App.MainVM is not null)
        {
            if (App.MainVM.DownloadOnlineCommand.CanExecute(meta))
                App.MainVM.DownloadOnlineCommand.Execute(meta);
        }
    }
}
