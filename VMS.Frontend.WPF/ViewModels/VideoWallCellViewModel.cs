using CommunityToolkit.Mvvm.ComponentModel;

namespace VMS.Frontend.WPF.ViewModels;

/// <summary>One cell of a saved Video Wall layout — a rectangle of grid units (Row/Column is its
/// top-left corner, RowSpan/ColumnSpan its size) that optionally shows one camera. Deliberately a
/// separate type from GridSlotViewModel: GridSlotViewModel is rebuilt from scratch on every
/// MainViewModel.RebuildGridSlots call and its Index only keys into the ephemeral, in-memory
/// Live View _slotAssignments dictionary, while this holds persisted geometry that only changes
/// through the layout editor's merge/split commands.</summary>
public partial class VideoWallCellViewModel(Guid id) : ObservableObject
{
    public Guid Id { get; } = id;

    [ObservableProperty] private int _row;
    [ObservableProperty] private int _column;
    [ObservableProperty] private int _rowSpan = 1;
    [ObservableProperty] private int _columnSpan = 1;

    [ObservableProperty] private Guid? _cameraId;
    [ObservableProperty] private string? _cameraCode;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLabel))]
    private string? _cameraName;

    /// <summary>Resolved against MainViewModel.Cameras by Code when displaying the wall (null in
    /// the layout editor, which only shows the camera's name — it never touches a real player).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private CameraTileViewModel? _tile;

    [ObservableProperty] private bool _isSelected;

    /// <summary>Used by the wall display's DataTemplate (mirrors GridSlotViewModel.IsEmpty) —
    /// whether there's a live tile to render, not just whether a camera is assigned.</summary>
    public bool IsEmpty => Tile is null;

    /// <summary>Used by the layout editor, which never resolves a Tile — just shows the assigned
    /// camera's name, or a placeholder when the cell is empty.</summary>
    public string DisplayLabel => CameraName ?? "—";
}
