using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.ViewModels;

namespace VMS.Frontend.WPF.Views;

public partial class EditVideoWallLayoutWindow : Window
{
    private Grid? _cellsGridPanel;

    public EditVideoWallLayoutWindow()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RebuildGridDefinitions();
    }

    private void CellsGridPanel_Loaded(object sender, RoutedEventArgs e)
    {
        _cellsGridPanel = (Grid)sender;
        RebuildGridDefinitions();
    }

    private void OnApplyGridSizeClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is EditVideoWallLayoutViewModel vm)
        {
            vm.ApplyGridSizeCommand.Execute(null);
            RebuildGridDefinitions();
        }
    }

    private void OnMergeRightClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is EditVideoWallLayoutViewModel vm)
        {
            vm.MergeRightCommand.Execute(null);
        }
    }

    private void OnMergeDownClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is EditVideoWallLayoutViewModel vm)
        {
            vm.MergeDownCommand.Execute(null);
        }
    }

    private void OnSplitClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is EditVideoWallLayoutViewModel vm)
        {
            vm.SplitCommand.Execute(null);
            RebuildGridDefinitions();
        }
    }

    private void CameraItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CameraDto camera } && DataContext is EditVideoWallLayoutViewModel vm)
        {
            vm.AssignCameraToSelectedCellCommand.Execute(camera);
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is EditVideoWallLayoutViewModel vm)
        {
            await vm.SaveCommand.ExecuteAsync(null);
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>Rebuilds the cell grid panel's Row/ColumnDefinitions to match the current cells'
    /// bounds — computed from the cells themselves (not the Rows/Columns text boxes) so a
    /// cancelled "Apply size" (grid-reset confirmation declined) never desyncs the panel from
    /// what's actually displayed.</summary>
    private void RebuildGridDefinitions()
    {
        if (_cellsGridPanel is null || DataContext is not EditVideoWallLayoutViewModel vm || vm.Cells.Count == 0)
        {
            return;
        }

        var rows = vm.Cells.Max(c => c.Row + c.RowSpan);
        var columns = vm.Cells.Max(c => c.Column + c.ColumnSpan);

        _cellsGridPanel.RowDefinitions.Clear();
        _cellsGridPanel.ColumnDefinitions.Clear();
        for (var i = 0; i < rows; i++)
        {
            _cellsGridPanel.RowDefinitions.Add(new RowDefinition());
        }
        for (var i = 0; i < columns; i++)
        {
            _cellsGridPanel.ColumnDefinitions.Add(new ColumnDefinition());
        }
    }
}
