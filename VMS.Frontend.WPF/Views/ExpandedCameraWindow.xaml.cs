using System.Windows;
using System.Windows.Input;
using VMS.Frontend.WPF.ViewModels;

namespace VMS.Frontend.WPF.Views;

public partial class ExpandedCameraWindow : Window
{
    public ExpandedCameraWindow()
    {
        InitializeComponent();

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };

        DataContextChanged += (_, args) =>
        {
            if (args.NewValue is ExpandedCameraViewModel vm)
            {
                vm.CloseRequested += Close;
            }
        };
    }

    private void Header_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ExpandedCameraViewModel vm)
        {
            vm.CloseCommand.Execute(null);
        }
    }
}
