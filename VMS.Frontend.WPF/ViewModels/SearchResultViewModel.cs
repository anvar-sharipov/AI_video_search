using VMS.Frontend.WPF.Api;

namespace VMS.Frontend.WPF.ViewModels;

public class SearchResultViewModel(SearchResultDto dto)
{
    public SearchResultDto Dto { get; } = dto;

    public string CameraId => dto.CameraId;
    public string ObjectType => dto.ObjectType;
    public string? ColorAttribute => dto.ColorAttribute;
    public DateTimeOffset Timestamp => dto.Timestamp;
    public double Confidence => dto.Confidence;

    public string Summary =>
        $"{dto.CameraId} — {(dto.ColorAttribute is null ? "" : dto.ColorAttribute + " ")}{dto.ObjectType} " +
        $"({dto.Confidence:P0}) @ {dto.Timestamp:HH:mm:ss}";
}
