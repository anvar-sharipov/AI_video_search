using System.Windows;
using VMS.Frontend.WPF.ViewModels;

namespace VMS.Frontend.WPF.Views;

public partial class EditCameraWindow : Window
{
    public EditCameraWindow()
    {
        InitializeComponent();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is EditCameraViewModel vm)
        {
            await vm.SaveCommand.ExecuteAsync(PasswordBox.Password);
        }
    }
}
