using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.Services;

namespace VMS.Frontend.WPF.ViewModels;

/// <summary>
/// Backs the Audio screen. Listening to a camera's audio track is genuinely implemented (LibVLC
/// decodes whatever audio track the RTSP stream carries — every other player in this client
/// plays video-only because their Media never gets an unmuted audio-consuming player attached,
/// not because audio is actively suppressed). Two-way broadcast is honestly NOT implemented:
/// that needs a camera's vendor-specific two-way-audio protocol (most consumer/ONVIF cameras
/// expose it via a proprietary HTTP endpoint, e.g. Hikvision's ISAPI, not a generic RTSP or ONVIF
/// call this project already speaks) — CanBroadcast stays false and the UI explains why rather
/// than shipping a button that would silently do nothing.
/// </summary>
public partial class AudioViewModel : ObservableObject, IDisposable
{
    private readonly ApiClient _api;
    private readonly LibVLC _libVlc;
    private Media? _media;

    public MediaPlayer Player { get; }
    public ObservableCollection<CameraDto> Cameras { get; } = [];

    [ObservableProperty] private CameraDto? _selectedCamera;
    [ObservableProperty] private bool _isListening;
    [ObservableProperty] private string _statusMessage = string.Empty;

    // --- Archive: listen to / download a past recording's audio (see class doc comment — the
    // recorder never strips audio from a segment, and this player is never muted, so a past
    // recording's audio plays exactly like the live stream's does). ---
    [ObservableProperty] private DateTime _archiveDate = DateTime.Today;
    [ObservableProperty] private string _archiveFromTime = "00:00";
    [ObservableProperty] private string _archiveToTime = "00:01";
    [ObservableProperty] private bool _isArchiveBusy;

    /// <summary>Always false — see the class doc comment. Exposed as a property (not a literal in XAML) so the reason shows up in one place if this ever becomes true for a specific camera vendor.</summary>
    public bool CanBroadcast => false;

    public AudioViewModel(ApiClient api)
    {
        _api = api;
        _libVlc = new LibVLC();
        Player = new MediaPlayer(_libVlc) { Mute = false };
        _ = LoadCamerasAsync();
    }

    private async Task LoadCamerasAsync()
    {
        try
        {
            Cameras.Clear();
            foreach (var c in await _api.GetCamerasAsync())
            {
                Cameras.Add(c);
            }
        }
        catch (ApiException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ListenAsync()
    {
        if (SelectedCamera is null)
        {
            return;
        }

        try
        {
            var status = await _api.GetCameraStatusAsync(SelectedCamera.Code);
            if (status is null)
            {
                StatusMessage = LocalizationService.Get("Audio_CameraNotRunning");
                return;
            }

            Player.Stop();
            _media?.Dispose();
            _media = new Media(_libVlc, status.MainStreamUri, FromType.FromLocation);
            // This screen has no VideoView to give the video track anywhere to render — without
            // this option, LibVLC falls back to popping open its own native top-level window
            // ("VLC (Direct3D11 output)") for the video, a window this app never wired a close
            // handler for. Audio-only listening never needs the video track decoded at all.
            _media.AddOption(":no-video");
            Player.Play(_media);
            Player.Mute = false;
            Player.Volume = 100;
            IsListening = true;
            StatusMessage = string.Empty;
        }
        catch (ApiException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void StopListening()
    {
        Player.Stop();
        IsListening = false;
    }

    /// <summary>Shared by ListenToArchiveAsync and PrepareArchiveDownloadAsync — both need the
    /// same camera+time-range validation and the same POST /api/clips extraction, they just do
    /// different things with the resulting file.</summary>
    private async Task<string?> ExtractArchiveClipAsync()
    {
        if (SelectedCamera is null)
        {
            StatusMessage = LocalizationService.Get("Audio_SelectCameraError");
            return null;
        }

        if (!TimeSpan.TryParse(ArchiveFromTime, out var fromTime) || !TimeSpan.TryParse(ArchiveToTime, out var toTime))
        {
            StatusMessage = LocalizationService.Get("Archive_InvalidTimeError");
            return null;
        }

        var start = new DateTimeOffset(ArchiveDate.Date + fromTime, DateTimeOffset.Now.Offset);
        var durationSeconds = (int)(toTime - fromTime).TotalSeconds;
        if (durationSeconds <= 0)
        {
            StatusMessage = LocalizationService.Get("Audio_ArchiveRangeInvalid");
            return null;
        }

        return await _api.RequestClipAsync(new ClipRequestDto(SelectedCamera.Code, start, 0, durationSeconds));
    }

    [RelayCommand]
    private async Task ListenToArchiveAsync()
    {
        IsArchiveBusy = true;
        StatusMessage = string.Empty;
        try
        {
            var fileName = await ExtractArchiveClipAsync();
            if (fileName is null)
            {
                return;
            }

            Player.Stop();
            _media?.Dispose();
            _media = new Media(_libVlc, _api.GetClipDownloadUri(fileName));
            _media.AddOption(":no-video");
            Player.Play(_media);
            Player.Mute = false;
            Player.Volume = 100;
            IsListening = true;
        }
        catch (ApiException ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsArchiveBusy = false;
        }
    }

    /// <summary>Called from AudioWindow's code-behind after a SaveFileDialog picks the
    /// destination — extraction happens here (ViewModel), the dialog itself is code-behind's job,
    /// same split as ArchiveViewModel.SaveSnapshot.</summary>
    public async Task<byte[]?> PrepareArchiveDownloadAsync()
    {
        IsArchiveBusy = true;
        StatusMessage = string.Empty;
        try
        {
            var fileName = await ExtractArchiveClipAsync();
            return fileName is null ? null : await _api.DownloadClipBytesAsync(fileName);
        }
        catch (ApiException ex)
        {
            StatusMessage = ex.Message;
            return null;
        }
        finally
        {
            IsArchiveBusy = false;
        }
    }

    public void Dispose()
    {
        Player.Stop();
        Player.Dispose();
        _media?.Dispose();
        _libVlc.Dispose();
    }
}
