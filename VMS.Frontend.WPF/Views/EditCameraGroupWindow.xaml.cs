using System.Windows;
using VMS.Frontend.WPF.ViewModels;

namespace VMS.Frontend.WPF.Views;

public partial class EditCameraGroupWindow : Window
{
    public EditCameraGroupWindow()
    {
        InitializeComponent();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is EditCameraGroupViewModel vm)
        {
            await vm.SaveCommand.ExecuteAsync(null);
        }
    }
}
