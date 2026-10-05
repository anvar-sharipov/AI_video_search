using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    /// <summary>
    /// True only for the instant the freeze watchdog is dropping a stalled connection and
    /// re-playing it (see CheckFrozen) — a stalled RTSP connection often leaves LibVLC's D3D
    /// surface showing a torn/corrupted last frame instead of erroring out cleanly, so this also
    /// drives hiding that stale surface (XAML blanks the VideoView while frozen). The watchdog
    /// reconnects automatically, so this no longer stays true waiting for the user — the
    /// reconnect button stays only as a manual fallback (e.g. before any stream URI is known).
    /// </summary>
    [ObservableProperty]
    private bool _isFrozen;

    public MediaPlayer Player { get; }

    /// <summary>High-res profile — used only by the fullscreen popup (see ExpandedCameraViewModel).</summary>
    public string? MainStreamUri { get; private set; }

    /// <summary>Live known/unknown overlay boxes (see PersonSightingEndpoints' detections
    /// endpoint) — refreshed on _detectionsTimer, drawn by the tile's XAML Canvas overlay.</summary>
    public ObservableCollection<LiveDetectionBoxViewModel> DetectionBoxes { get; } = [];

    private string? _subStreamUri;

    private readonly LibVLC _libVlc;
    private readonly ApiClient _api;
    private readonly DispatcherTimer _detectionsTimer;
    private readonly DispatcherTimer _freezeWatchdog;
    private Media? _media;

    private static readonly TimeSpan FreezeThreshold = TimeSpan.FromSeconds(6);
    private DateTime _lastFrameAtUtc = DateTime.UtcNow;

    /// <summary>True once this tile's VideoView has been realized at least once (native Hwnd
    /// exists) — playing before that leaves LibVLC with nowhere to render, so it pops its own
    /// top-level "VLC (Direct3D11 output)" window instead (same root cause the fullscreen popup
    /// had — see ExpandedCameraViewModel.StartPlayback). Grid-slot reassignment recreates the
    /// VideoView at a new position but never needs a second deferred play: the MediaPlayer is
    /// already attached and playing, and LibVLCSharp's VideoView reparents to the new Hwnd on its
    /// own once bound.</summary>
    private bool _viewReady;

    // Manual recording runs on its own LibVLC instance/connection to the camera, entirely separate
    // from the grid tile's own Player — so starting/stopping a local recording can never disturb
    // what's already on screen (no need to touch the live Media/Player at all).
    private LibVLC? _recordLibVlc;
    private MediaPlayer? _recordPlayer;
    private Media? _recordMedia;

    public CameraTileViewModel(LibVLC libVlc, CameraDto camera, ApiClient api)
    {
        _libVlc = libVlc;
        _api = api;
        _code = camera.Code;
        _name = camera.Name;
        Player = new MediaPlayer(libVlc);

        // TimeChanged fires on a libvlc-internal thread; it only touches a plain field here
        // (not an [ObservableProperty]), so no dispatcher hop is needed — CheckFrozen, which
        // does mutate bound state, always runs on _freezeWatchdog's own UI-thread Tick.
        Player.TimeChanged += (_, _) => _lastFrameAtUtc = DateTime.UtcNow;

        _detectionsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _detectionsTimer.Tick += async (_, _) => await RefreshDetectionsAsync();
        _detectionsTimer.Start();

        _freezeWatchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _freezeWatchdog.Tick += (_, _) => CheckFrozen();
        _freezeWatchdog.Start();
    }

    /// <summary>
    /// A tile that hasn't delivered a new frame in FreezeThreshold (whether still nominally
    /// "playing" a stalled connection, or already stopped after a previous failed attempt) is
    /// treated as stalled: drop LibVLC's decode/render surface and reconnect automatically, so
    /// the tile recovers on its own instead of sitting frozen until someone notices and clicks
    /// reconnect. PlayStream resets the stall clock, so a still-unreachable camera just keeps
    /// retrying every FreezeThreshold. Does nothing while manually paused or not online at all —
    /// those are intentional states, not stalls.
    /// </summary>
    private void CheckFrozen()
    {
        if (IsPaused || !IsOnline)
        {
            return;
        }

        if (DateTime.UtcNow - _lastFrameAtUtc <= FreezeThreshold)
        {
            return;
        }

        Player.Stop();
        if (_subStreamUri is not null)
        {
            PlayStream(_subStreamUri);
        }
        else
        {
            IsFrozen = true;
        }
    }

    /// <summary>Left-side reconnect button's command, visible only while IsFrozen — re-plays the same sub-stream from scratch.</summary>
    [RelayCommand]
    private void Reconnect()
    {
        IsFrozen = false;
        if (_subStreamUri is not null)
        {
            PlayStream(_subStreamUri);
        }
    }

    /// <summary>Polls the last few seconds of "person" detections for this camera and refreshes
    /// DetectionBoxes with only the recognized ones (PersonName set) — an unmatched face draws no
    /// box at all, rather than an "unknown" box for every face in frame. Best-effort: a failed
    /// poll (camera offline, server briefly unreachable) just leaves the previous boxes on screen
    /// a little longer rather than erroring the tile.</summary>
    private async Task RefreshDetectionsAsync()
    {
        try
        {
            var detections = await _api.GetLiveDetectionsAsync(Code);
            DetectionBoxes.Clear();
            foreach (var d in detections.Where(d => d.PersonName is not null))
            {
                DetectionBoxes.Add(new LiveDetectionBoxViewModel(d.X, d.Y, d.Width, d.Height, d.PersonName));
            }
        }
        catch (ApiException)
        {
            // Transient — next tick tries again.
        }
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
        if (_viewReady)
        {
            Player.Play(_media);
        }
        IsOnline = true;
        IsPaused = false;
        IsFrozen = false;
        _lastFrameAtUtc = DateTime.UtcNow;
    }

    /// <summary>Called from the grid tile's VideoView.Loaded — see the _viewReady field comment.</summary>
    public void NotifyViewLoaded()
    {
        if (_viewReady)
        {
            return;
        }

        _viewReady = true;
        if (_media is not null)
        {
            Player.Play(_media);
        }
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
        _detectionsTimer.Stop();
        _freezeWatchdog.Stop();
        StopRecording();
        Player.Stop();
        Player.Dispose();
        _media?.Dispose();
    }
}
