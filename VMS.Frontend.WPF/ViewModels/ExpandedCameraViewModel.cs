using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;

namespace VMS.Frontend.WPF.ViewModels;

/// <summary>
/// Backs the fullscreen single-camera popup (main-stream quality). Deliberately gets its own
/// brand-new MediaPlayer/Media/VideoView (via a new top-level Window) instead of reusing the
/// grid tile's VideoView — swapping MediaPlayer on an already-rendering grid VideoView left
/// some cameras' Direct3D11 output rendering solid white with no LibVLC-reported error at all
/// (confirmed via LibVLC's own Playing/Buffering/Vout events all reporting success). A fresh
/// VideoView in a fresh window sidesteps whatever state that reuse was corrupting.
/// </summary>
public partial class ExpandedCameraViewModel : ObservableObject, IDisposable
{
    public string Name { get; }
    public MediaPlayer Player { get; }

    private readonly Media _media;

    public event Action? CloseRequested;

    public ExpandedCameraViewModel(LibVLC libVlc, string name, string mainStreamUri)
    {
        Name = name;
        Player = new MediaPlayer(libVlc);
        _media = new Media(libVlc, mainStreamUri, FromType.FromLocation);
        _media.AddOption(":avcodec-hw=none");
        Player.Play(_media);
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();

    public void Dispose()
    {
        Player.Stop();
        Player.Dispose();
        _media.Dispose();
    }
}
