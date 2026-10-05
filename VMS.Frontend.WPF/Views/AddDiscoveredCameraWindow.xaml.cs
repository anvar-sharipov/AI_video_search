using System.Windows;
using VMS.Frontend.WPF.ViewModels;

namespace VMS.Frontend.WPF.Views;

public partial class AddDiscoveredCameraWindow : Window
{
    public AddDiscoveredCameraWindow()
    {
        InitializeComponent();
    }

    /// <summary>Selects the pre-filled Name (defaults to the discovered model) so typing
    /// replaces it immediately, instead of inserting at whatever cursor position the click
    /// happened to land on — a plain unfocused TextBox gives no visual hint where that is.</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is AddDiscoveredCameraViewModel vm)
        {
            await vm.SaveCommand.ExecuteAsync(PasswordBox.Password);
        }
    }
}
