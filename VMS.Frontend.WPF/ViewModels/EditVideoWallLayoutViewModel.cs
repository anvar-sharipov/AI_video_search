using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.Services;

namespace VMS.Frontend.WPF.ViewModels;

/// <summary>Create-or-edit a VideoWallLayout: a name, a Rows x Columns grid of cells (some
/// possibly merged into bigger rectangles), and a camera assigned per cell. One VM for both Add
/// (layout is null) and Edit, like EditCameraGroupViewModel.
///
/// Merge/Split model: the cell list IS the layout, there's no separate "template" — merging
/// absorbs exactly one whole adjacent cell of matching height/width into the selected cell
/// (Excel-style cell merge), and splitting always decomposes the selected cell back into plain
/// 1x1 cells (no merge history is kept; if the merged cell had a camera assigned, that
/// assignment is discarded along with the split — expected, not an error).</summary>
public partial class EditVideoWallLayoutViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private readonly Guid? _layoutId;

    public ObservableCollection<VideoWallCellViewModel> Cells { get; } = [];
    public ObservableCollection<CameraDto> Cameras { get; } = [];

    [ObservableProperty] private string _name;
    [ObservableProperty] private int _rows;
    [ObservableProperty] private int _columns;

    [ObservableProperty]
    private VideoWallCellViewModel? _selectedCell;

    [ObservableProperty] private bool _canMergeRight;
    [ObservableProperty] private bool _canMergeDown;
    [ObservableProperty] private bool _canSplit;

    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public event Action? Saved;

    public EditVideoWallLayoutViewModel(ApiClient api, VideoWallLayoutDto? layout)
    {
        _api = api;
        _layoutId = layout?.Id;
        _name = layout?.Name ?? string.Empty;

        if (layout is not null)
        {
            _rows = layout.Rows;
            _columns = layout.Columns;
            foreach (var cell in layout.Cells)
            {
                Cells.Add(new VideoWallCellViewModel(cell.Id)
                {
                    Row = cell.Row,
                    Column = cell.Column,
                    RowSpan = cell.RowSpan,
                    ColumnSpan = cell.ColumnSpan,
                    CameraId = cell.CameraId,
                    CameraCode = cell.CameraCode,
                    CameraName = cell.CameraName
                });
            }
        }
        else
        {
            _rows = 2;
            _columns = 2;
            ResetGrid();
        }

        _ = LoadCamerasAsync();
    }

    private async Task LoadCamerasAsync()
    {
        try
        {
            foreach (var camera in await _api.GetCamerasAsync())
            {
                Cameras.Add(camera);
            }
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void ApplyGridSize()
    {
        if (Rows <= 0 || Columns <= 0)
        {
            ErrorMessage = LocalizationService.Get("VideoWallLayout_InvalidSizeError");
            return;
        }

        if (Cells.Count > 0)
        {
            var confirmed = MessageBox.Show(
                LocalizationService.Get("VideoWallLayout_ResetGridConfirm"),
                LocalizationService.Get("Win_EditVideoWallLayout_Title"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
            if (!confirmed)
            {
                return;
            }
        }

        ResetGrid();
    }

    private void ResetGrid()
    {
        Cells.Clear();
        SelectedCell = null;
        for (var r = 0; r < Rows; r++)
        {
            for (var c = 0; c < Columns; c++)
            {
                Cells.Add(new VideoWallCellViewModel(Guid.NewGuid()) { Row = r, Column = c });
            }
        }
    }

    [RelayCommand]
    private void SelectCell(VideoWallCellViewModel cell)
    {
        if (SelectedCell is not null)
        {
            SelectedCell.IsSelected = false;
        }
        SelectedCell = cell;
        cell.IsSelected = true;
    }

    [RelayCommand]
    private void AssignCameraToSelectedCell(CameraDto? camera)
    {
        if (camera is null)
        {
            return;
        }
        if (SelectedCell is null)
        {
            ErrorMessage = LocalizationService.Get("VideoWallLayout_SelectCellFirstError");
            return;
        }

        // A camera can only show in one cell at a time — assigning it here vacates wherever it was.
        foreach (var other in Cells.Where(c => c.CameraId == camera.Id))
        {
            other.CameraId = null;
            other.CameraCode = null;
            other.CameraName = null;
        }

        SelectedCell.CameraId = camera.Id;
        SelectedCell.CameraCode = camera.Code;
        SelectedCell.CameraName = camera.Name;
    }

    [RelayCommand]
    private void MergeRight()
    {
        if (SelectedCell is null)
        {
            return;
        }

        var neighbor = FindCellAt(SelectedCell.Row, SelectedCell.Column + SelectedCell.ColumnSpan);
        if (neighbor is null || neighbor.RowSpan != SelectedCell.RowSpan)
        {
            return;
        }

        SelectedCell.ColumnSpan += neighbor.ColumnSpan;
        Cells.Remove(neighbor);
        UpdateMergeSplitState();
    }

    [RelayCommand]
    private void MergeDown()
    {
        if (SelectedCell is null)
        {
            return;
        }

        var neighbor = FindCellAt(SelectedCell.Row + SelectedCell.RowSpan, SelectedCell.Column);
        if (neighbor is null || neighbor.ColumnSpan != SelectedCell.ColumnSpan)
        {
            return;
        }

        SelectedCell.RowSpan += neighbor.RowSpan;
        Cells.Remove(neighbor);
        UpdateMergeSplitState();
    }

    [RelayCommand]
    private void Split()
    {
        if (SelectedCell is null || (SelectedCell.RowSpan == 1 && SelectedCell.ColumnSpan == 1))
        {
            return;
        }

        var cell = SelectedCell;
        Cells.Remove(cell);
        for (var r = cell.Row; r < cell.Row + cell.RowSpan; r++)
        {
            for (var c = cell.Column; c < cell.Column + cell.ColumnSpan; c++)
            {
                Cells.Add(new VideoWallCellViewModel(Guid.NewGuid()) { Row = r, Column = c });
            }
        }

        SelectedCell = null;
    }

    partial void OnSelectedCellChanged(VideoWallCellViewModel? value) => UpdateMergeSplitState();

    private VideoWallCellViewModel? FindCellAt(int row, int column) =>
        Cells.FirstOrDefault(c => c.Row == row && c.Column == column);

    private void UpdateMergeSplitState()
    {
        if (SelectedCell is null)
        {
            CanMergeRight = false;
            CanMergeDown = false;
            CanSplit = false;
            return;
        }

        var rightNeighbor = FindCellAt(SelectedCell.Row, SelectedCell.Column + SelectedCell.ColumnSpan);
        CanMergeRight = rightNeighbor is not null && rightNeighbor.RowSpan == SelectedCell.RowSpan;

        var downNeighbor = FindCellAt(SelectedCell.Row + SelectedCell.RowSpan, SelectedCell.Column);
        CanMergeDown = downNeighbor is not null && downNeighbor.ColumnSpan == SelectedCell.ColumnSpan;

        CanSplit = SelectedCell.RowSpan > 1 || SelectedCell.ColumnSpan > 1;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = LocalizationService.Get("VideoWallLayout_NameRequiredError");
            return;
        }

        var cellRequests = Cells.Select(c =>
            new SaveVideoWallCellRequestDto(c.Row, c.Column, c.RowSpan, c.ColumnSpan, c.CameraId)).ToList();
        var request = new SaveVideoWallLayoutRequestDto(Name.Trim(), Rows, Columns, cellRequests);

        IsBusy = true;
        try
        {
            if (_layoutId is { } id)
            {
                await _api.UpdateVideoWallLayoutAsync(id, request);
            }
            else
            {
                await _api.CreateVideoWallLayoutAsync(request);
            }
            Saved?.Invoke();
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
