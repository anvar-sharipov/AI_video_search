using System.IO;
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

    /// <summary>Drives the highlighted border when this tile is the single-clicked "active" tile in the grid.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>True once the bottom toolbar's pause button has frozen this tile's decode — Play() with no argument resumes the same Media.</summary>
    [ObservableProperty]
    private bool _isPaused;

    /// <summary>True while a manual local recording (bottom toolbar's record button) is writing this tile's sub-stream to disk.</summary>
    [ObservableProperty]
    private bool _isRecording;

    public MediaPlayer Player { get; }

    /// <summary>High-res profile — used only by the fullscreen popup (see ExpandedCameraViewModel).</summary>
    public string? MainStreamUri { get; private set; }

    private string? _subStreamUri;

    private readonly LibVLC _libVlc;
    private Media? _media;

    // Manual recording runs on its own LibVLC instance/connection to the camera, entirely separate
    // from the grid tile's own Player — so starting/stopping a local recording can never disturb
    // what's already on screen (no need to touch the live Media/Player at all).
    private LibVLC? _recordLibVlc;
    private MediaPlayer? _recordPlayer;
    private Media? _recordMedia;

    public CameraTileViewModel(LibVLC libVlc, CameraDto camera)
    {
        _libVlc = libVlc;
        _code = camera.Code;
        _name = camera.Name;
        Player = new MediaPlayer(libVlc);
    }

    public void SetStreamUris(string mainStreamUri, string subStreamUri)
    {
        MainStreamUri = mainStreamUri;
        _subStreamUri = subStreamUri;
        PlayStream(subStreamUri);
    }

    public void PlayStream(string rtspUri)
    {
        _media?.Dispose();
        _media = new Media(_libVlc, rtspUri, FromType.FromLocation);
        _media.AddOption(":avcodec-hw=none");
        Player.Play(_media);
        IsOnline = true;
        IsPaused = false;
    }

    /// <summary>Bottom toolbar's ✕ "stop all": halts decode/display, leaving the tile blank until Resume() is called.</summary>
    public void Stop()
    {
        Player.Stop();
        IsOnline = false;
        IsPaused = false;
    }

    /// <summary>Bottom toolbar's ⏸: freeze the current frame without dropping the connection.</summary>
    public void TogglePause()
    {
        if (Player.IsPlaying)
        {
            Player.Pause();
            IsPaused = true;
        }
        else
        {
            // Covers both "resume after pause" and "resume after Stop()" — the MediaPlayer keeps
            // its last-assigned Media, so a bare Play() restarts the same stream either way.
            Player.Play();
            IsPaused = false;
            IsOnline = true;
        }
    }

    /// <summary>
    /// Bottom toolbar's ⏺: connects a second time to this tile's sub-stream purely to remux it to
    /// an mp4 file (no re-encode) — mirrors a typical NVR client's "manual record" of the live view.
    /// </summary>
    public string? StartRecording(string outputDirectory)
    {
        if (IsRecording || _subStreamUri is null)
        {
            return null;
        }

        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, $"{Code}_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
        var sanitizedPath = path.Replace('\\', '/');

        _recordLibVlc = new LibVLC("--avcodec-hw=none");
        _recordPlayer = new MediaPlayer(_recordLibVlc);
        _recordMedia = new Media(_recordLibVlc, _subStreamUri, FromType.FromLocation);
        _recordMedia.AddOption($":sout=#std{{access=file,mux=mp4,dst={sanitizedPath}}}");
        _recordMedia.AddOption(":sout-keep");
        _recordPlayer.Play(_recordMedia);
        IsRecording = true;
        return path;
    }

    public void StopRecording()
    {
        if (!IsRecording)
        {
            return;
        }

        _recordPlayer?.Stop();
        _recordPlayer?.Dispose();
        _recordMedia?.Dispose();
        _recordLibVlc?.Dispose();
        _recordPlayer = null;
        _recordMedia = null;
        _recordLibVlc = null;
        IsRecording = false;
    }

    /// <summary>Snapshot of the current frame, PNG at source resolution (width/height 0 = original size).</summary>
    public bool TakeSnapshot(string path) => Player.IsPlaying && Player.TakeSnapshot(0, path, 0, 0);

    public void Dispose()
    {
        StopRecording();
        Player.Stop();
        Player.Dispose();
        _media?.Dispose();
    }
}
