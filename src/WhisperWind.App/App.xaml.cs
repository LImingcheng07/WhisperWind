using System.Windows;
using Wpf.Ui;

namespace WhisperWind.App;

public partial class App : Application
{
    public static SnackbarService Snackbar { get; private set; } = null!;
    public static ViewModels.MainViewModel? MainVM { get; set; }
}
