using System.Windows;
using VMS.Frontend.WPF.ViewModels;

namespace VMS.Frontend.WPF.Views;

public partial class AddCameraWindow : Window
{
    public AddCameraWindow()
    {
        InitializeComponent();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is AddCameraViewModel vm)
        {
            await vm.SaveCommand.ExecuteAsync(PasswordBox.Password);
        }
    }
}
