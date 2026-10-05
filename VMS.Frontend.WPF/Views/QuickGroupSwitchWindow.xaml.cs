using System.Windows;
using System.Windows.Controls;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.Services;
using VMS.Frontend.WPF.ViewModels;

namespace VMS.Frontend.WPF.Views;

/// <summary>A plain owned Window, not a Popup — this session already established that WPF
/// Popup content isn't reliably clickable in this app (RenderOptions.ProcessRenderMode =
/// SoftwareOnly, see App.xaml.cs), so every "quick picker" flow here uses a real Window.</summary>
public partial class QuickGroupSwitchWindow : Window
{
    private record Item(string Label, HashSet<string>? CameraCodes);

    private readonly ApiClient _api;
    private readonly MainViewModel _mainVm;

    public QuickGroupSwitchWindow(ApiClient api, MainViewModel mainVm)
    {
        InitializeComponent();
        _api = api;
        _mainVm = mainVm;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        StatusText.Text = LocalizationService.Get("QuickGroupSwitch_Loading");
        var items = new List<Item> { new(LocalizationService.Get("QuickGroupSwitch_AllCameras"), null) };
        try
        {
            var groups = await _api.GetCameraGroupsAsync();
            items.AddRange(groups.Select(g => new Item(g.Name, g.Members.Select(m => m.Code).ToHashSet())));
            StatusText.Text = string.Empty;
        }
        catch (ApiException ex)
        {
            StatusText.Text = ex.Message;
        }

        GroupsListBox.ItemsSource = items;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GroupsListBox.SelectedItem is Item item)
        {
            _mainVm.ApplyCameraGroupFilter(item.CameraCodes);
            Close();
        }
    }
}
