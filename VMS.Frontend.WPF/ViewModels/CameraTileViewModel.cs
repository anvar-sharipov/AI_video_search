using CommunityToolkit.Mvvm.ComponentModel;
using LibVLCSharp.Shared;
using VMS.Frontend.WPF.Api;

namespace VMS.Frontend.WPF.ViewModels;

/// <summary>One live grid tile: its own MediaPlayer against the camera's sub-stream (grid = low-res tiles, per the dual-stream design).</summary>
public partial class CameraTileViewModel : ObservableObject, IDisposable
{
    [ObservableProperty]
    private string _code;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isOnline;

    public MediaPlayer Player { get; }

    private readonly LibVLC _libVlc;
    private Media? _media;

    public CameraTileViewModel(LibVLC libVlc, CameraDto camera)
    {
        _libVlc = libVlc;
        _code = camera.Code;
        _name = camera.Name;
        Player = new MediaPlayer(libVlc);
    }

    public void PlayStream(string rtspUri)
    {
        _media?.Dispose();
        _media = new Media(_libVlc, rtspUri, FromType.FromLocation);
        Player.Play(_media);
        IsOnline = true;
    }

    public void Stop()
    {
        Player.Stop();
        IsOnline = false;
    }

    public void Dispose()
    {
        Player.Stop();
        Player.Dispose();
        _media?.Dispose();
    }
}
