using System.Windows;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.ViewModels;

namespace VMS.Frontend.WPF.Views;

public partial class FaceRecognitionWindow : Window
{
    private readonly ApiClient _api;

    public FaceRecognitionWindow(ApiClient api)
    {
        InitializeComponent();
        _api = api;

        DataContextChanged += (_, args) =>
        {
            if (args.NewValue is FaceRecognitionViewModel vm)
            {
                vm.ClipRequested += OnClipRequested;
            }
        };
    }

    private void OnClipRequested(SearchResultViewModel result)
    {
        var clipVm = new ClipPopupViewModel(_api, result);
        var popup = new ClipPopupWindow { Owner = this, DataContext = clipVm };
        popup.Closed += (_, _) => clipVm.Dispose();
        popup.Show();
    }
}
