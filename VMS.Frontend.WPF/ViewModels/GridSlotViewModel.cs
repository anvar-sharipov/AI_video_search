using CommunityToolkit.Mvvm.ComponentModel;

namespace VMS.Frontend.WPF.ViewModels;

/// <summary>
/// One position in the live-view grid. References an already-playing CameraTileViewModel from
/// MainViewModel.Cameras rather than owning any player itself — assigning/moving a camera
/// between slots never touches its MediaPlayer, only which slot currently displays it.
/// </summary>
public partial class GridSlotViewModel(int index) : ObservableObject
{
    public int Index { get; } = index;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private CameraTileViewModel? _tile;

    [ObservableProperty] private bool _isSelected;

    public bool IsEmpty => Tile is null;
}
