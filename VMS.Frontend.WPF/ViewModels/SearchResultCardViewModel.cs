using CommunityToolkit.Mvvm.ComponentModel;
using VMS.Frontend.WPF.Api;

namespace VMS.Frontend.WPF.ViewModels;

/// <summary>
/// One card in the Archive/Playback search-results grid: a SearchResultDto plus its
/// on-demand thumbnail (loaded async from GET /api/events/{id}/thumbnail). Separate from
/// the plain SearchResultViewModel (used by the list-row screens) because a WrapPanel of
/// many cards each needs its own observable thumbnail byte array.
/// </summary>
public partial class SearchResultCardViewModel : ObservableObject
{
    public SearchResultDto Dto { get; }

    public string CameraId => Dto.CameraId;
    public string ObjectType => Dto.ObjectType;
    public string? ColorAttribute => Dto.ColorAttribute;
    public string? PersonName => Dto.PersonName;
    public string? PlateNumber => Dto.PlateNumber;
    public DateTimeOffset Timestamp => Dto.Timestamp;
    public double Confidence => Dto.Confidence;
    public double? MatchScore => Dto.MatchScore;

    /// <summary>Only ever non-empty for a real Elasticsearch kNN score (face/reverse-image search) — never shown for plain attribute/text search results.</summary>
    public string MatchScoreText => MatchScore is { } score ? $"{score:P1}" : string.Empty;

    public bool HasMatchScore => MatchScore is not null;

    public string Summary =>
        $"{Dto.CameraId} — {(ColorAttribute is null ? "" : ColorAttribute + " ")}{ObjectType}" +
        $"{(PersonName is null ? "" : $" \"{PersonName}\"")}{(PlateNumber is null ? "" : $" [{PlateNumber}]")}" +
        $" @ {Timestamp:HH:mm:ss}";

    [ObservableProperty] private byte[]? _thumbnail;

    public SearchResultCardViewModel(ApiClient api, SearchResultDto dto)
    {
        Dto = dto;
        _ = LoadThumbnailAsync(api);
    }

    private async Task LoadThumbnailAsync(ApiClient api)
    {
        try
        {
            Thumbnail = await api.GetEventThumbnailBytesAsync(Dto.Id);
        }
        catch (ApiException)
        {
            // No thumbnail (retention already deleted the covering segment) — the card
            // just shows its text fields with no image, not an error.
        }
    }
}
