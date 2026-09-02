using CommunityToolkit.Mvvm.ComponentModel;
using LibVLCSharp.Shared;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.Services;

namespace VMS.Frontend.WPF.ViewModels;

/// <summary>Requests a 5-second clip for a search hit and loops it (MediaEnded -> replay), per the spec's popup player.</summary>
public partial class ClipPopupViewModel : ObservableObject, IDisposable
{
    private readonly ApiClient _api;
    private readonly LibVLC _libVlc;
    private Media? _media;

    public MediaPlayer Player { get; }

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _statusMessage = LocalizationService.Get("Clip_Loading");

    public ClipPopupViewModel(ApiClient api, SearchResultViewModel result)
    {
        _api = api;
        _libVlc = new LibVLC();
        Player = new MediaPlayer(_libVlc);
        Player.EndReached += OnEndReached;
        Title = result.Summary;

        _ = LoadClipAsync(result.Dto);
    }

    private async Task LoadClipAsync(SearchResultDto dto)
    {
        try
        {
            var fileName = await _api.RequestClipAsync(new ClipRequestDto(dto.CameraId, dto.Timestamp, null, null));
            var uri = _api.GetClipDownloadUri(fileName);

            _media = new Media(_libVlc, uri);
            Player.Play(_media);
            StatusMessage = string.Empty;
        }
        catch (ApiException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        // EndReached fires on a libvlc-internal thread; hop back via ThreadPool
        // since MediaPlayer.Play must not be called re-entrantly from that callback.
        _ = System.Threading.Tasks.Task.Run(() => Player.Play(_media));
    }

    public void Dispose()
    {
        Player.EndReached -= OnEndReached;
        Player.Stop();
        Player.Dispose();
        _media?.Dispose();
        _libVlc.Dispose();
    }
}
