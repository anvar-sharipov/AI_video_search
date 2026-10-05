using System.Windows;
using VMS.Frontend.WPF.ViewModels;

namespace VMS.Frontend.WPF.Views;

public partial class EditUserWindow : Window
{
    public EditUserWindow()
    {
        InitializeComponent();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is EditUserViewModel vm)
        {
            await vm.SaveCommand.ExecuteAsync(PasswordBox.Password);
        }
    }
}
