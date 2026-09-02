using System.Windows;
using VMS.Frontend.WPF.ViewModels;

namespace VMS.Frontend.WPF.Views;

public partial class OperationLogWindow : Window
{
    public OperationLogWindow()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is OperationLogViewModel vm)
        {
            await vm.LoadCommand.ExecuteAsync(null);
        }
    }
}
