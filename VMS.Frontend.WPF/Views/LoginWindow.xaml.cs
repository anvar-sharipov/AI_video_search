using System.Windows;
using VMS.Frontend.WPF.ViewModels;

namespace VMS.Frontend.WPF.Views;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
    }

    private async void OnLoginClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm)
        {
            await vm.LoginCommand.ExecuteAsync(PasswordBox.Password);
        }
    }
}
