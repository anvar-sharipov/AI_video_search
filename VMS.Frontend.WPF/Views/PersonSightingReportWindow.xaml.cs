using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.ViewModels;

namespace VMS.Frontend.WPF.Views;

public partial class PersonSightingReportWindow : Window
{
    private readonly ApiClient _api;

    public PersonSightingReportWindow(ApiClient api)
    {
        InitializeComponent();
        _api = api;

        DataContextChanged += (_, args) =>
        {
            if (args.OldValue is PersonSightingReportViewModel oldVm)
            {
                oldVm.SightingsRequested -= OnSightingsRequested;
            }

            if (args.NewValue is PersonSightingReportViewModel newVm)
            {
                newVm.SightingsRequested += OnSightingsRequested;
            }
        };
    }

    private void OnSightingsRequested(PersonSightingReportRowDto row)
    {
        var galleryVm = new PersonSightingGalleryViewModel(_api, row);
        var window = new PersonSightingGalleryWindow { Owner = this, DataContext = galleryVm };
        window.Show();
    }

    private void ReportGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is PersonSightingReportViewModel vm && ReportGrid.SelectedItem is PersonSightingReportRowDto row)
        {
            vm.ShowSightingsCommand.Execute(row);
        }
    }
}
