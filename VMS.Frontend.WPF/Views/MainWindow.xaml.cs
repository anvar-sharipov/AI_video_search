using System.Windows;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.ViewModels;

namespace VMS.Frontend.WPF.Views;

public partial class MainWindow : Window
{
    private readonly ApiClient _api;

    public MainWindow(ApiClient api)
    {
        InitializeComponent();
        _api = api;

        DataContextChanged += (_, args) =>
        {
            if (args.OldValue is MainViewModel oldVm)
            {
                oldVm.ClipRequested -= OnClipRequested;
                oldVm.AddCameraRequested -= OnAddCameraRequested;
            }

            if (args.NewValue is MainViewModel newVm)
            {
                newVm.ClipRequested += OnClipRequested;
                newVm.AddCameraRequested += OnAddCameraRequested;
            }
        };

        Closed += (_, _) => (DataContext as MainViewModel)?.Dispose();
    }

    private void OnClipRequested(SearchResultViewModel result)
    {
        var clipVm = new ClipPopupViewModel(_api, result);
        var popup = new ClipPopupWindow { Owner = this, DataContext = clipVm };
        popup.Closed += (_, _) => clipVm.Dispose();
        popup.Show();
    }

    private void OnAddCameraRequested()
    {
        var addVm = new AddCameraViewModel(_api);
        var window = new AddCameraWindow { Owner = this, DataContext = addVm };

        addVm.Created += async () =>
        {
            window.Close();
            if (DataContext is MainViewModel mainVm)
            {
                await mainVm.LoadCamerasCommand.ExecuteAsync(null);
            }
        };

        window.ShowDialog();
    }
}
