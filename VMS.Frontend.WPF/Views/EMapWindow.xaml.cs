using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.ViewModels;

namespace VMS.Frontend.WPF.Views;

public partial class EMapWindow : Window
{
    public EMapWindow()
    {
        InitializeComponent();

        DataContextChanged += (_, args) =>
        {
            if (args.OldValue is EMapViewModel oldVm)
            {
                oldVm.PropertyChanged -= OnViewModelPropertyChanged;
            }

            if (args.NewValue is EMapViewModel newVm)
            {
                newVm.PropertyChanged += OnViewModelPropertyChanged;
                UpdateMapImage(newVm.MapImageBytes);
            }
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EMapViewModel.MapImageBytes) && DataContext is EMapViewModel vm)
        {
            UpdateMapImage(vm.MapImageBytes);
        }
    }

    private void UpdateMapImage(byte[]? bytes)
    {
        if (bytes is null)
        {
            MapImage.Source = null;
            return;
        }

        var bitmap = new BitmapImage();
        using var stream = new MemoryStream(bytes);
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        MapImage.Source = bitmap;
    }

    private void OnChooseImageClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Image files|*.jpg;*.jpeg;*.png;*.bmp;*.gif" };
        if (dialog.ShowDialog() == true && DataContext is EMapViewModel vm)
        {
            vm.NewMapImagePath = dialog.FileName;
        }
    }

    private async void PinCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not EMapViewModel { IsPlacingPin: true } vm || PinCanvas.ActualWidth <= 0)
        {
            return;
        }

        var pos = e.GetPosition(PinCanvas);
        await vm.PlacePinAsync(pos.X / PinCanvas.ActualWidth, pos.Y / PinCanvas.ActualHeight);
    }

    private void Pin_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EMapPinDto pin } && DataContext is EMapViewModel vm)
        {
            vm.ActivatePinCommand.Execute(pin);
            e.Handled = true;
        }
    }

    private void Pin_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EMapPinDto pin } && DataContext is EMapViewModel vm)
        {
            vm.RemovePinCommand.Execute(pin);
            e.Handled = true;
        }
    }
}
