using System.Windows;
using VMS.Frontend.WPF.ViewModels;

namespace VMS.Frontend.WPF.Views;

public partial class AddUserWindow : Window
{
    public AddUserWindow()
    {
        InitializeComponent();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is AddUserViewModel vm)
        {
            await vm.SaveCommand.ExecuteAsync(PasswordBox.Password);
        }
    }
}
