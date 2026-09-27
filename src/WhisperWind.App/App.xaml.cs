using System.Windows;
using Wpf.Ui;

namespace WhisperWind.App;

public partial class App : Application
{
    public static SnackbarService Snackbar { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Snackbar = new SnackbarService();
    }
}
