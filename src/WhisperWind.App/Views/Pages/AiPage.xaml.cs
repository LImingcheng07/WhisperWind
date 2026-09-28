using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Input;
using WhisperWind.App.ViewModels;

namespace WhisperWind.App.Views.Pages;

public partial class AiPage : UserControl
{
    public AiPage()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is MainViewModel vm)
                vm.ChatMessages.CollectionChanged += (_, a) =>
                {
                    if (a.Action == NotifyCollectionChangedAction.Add) Scroller.ScrollToEnd();
                };
        };
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainViewModel vm)
        {
            vm.AskAiCommand.Execute(null);
            e.Handled = true;
        }
    }
}
