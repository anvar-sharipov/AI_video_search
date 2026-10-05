using System.IO;
using VMS.Frontend.WPF.Api;

namespace VMS.Frontend.WPF.ViewModels;

public class SearchResultViewModel(SearchResultDto dto)
{
    public SearchResultDto Dto { get; } = dto;

    public string CameraId => dto.CameraId;
    public string ObjectType => dto.ObjectType;
    public string? ColorAttribute => dto.ColorAttribute;
    public string? PersonName => dto.PersonName;
    public string? PlateNumber => dto.PlateNumber;
    public DateTimeOffset Timestamp => dto.Timestamp;
    public double Confidence => dto.Confidence;

    /// <summary>Just the archive segment's filename (VideoChunkLocation is a full path) — what the protect/delete archive endpoints expect.</summary>
    public string ArchiveFileName => Path.GetFileName(dto.VideoChunkLocation);

    public string Summary =>
        $"{dto.CameraId} — {(dto.ColorAttribute is null ? "" : dto.ColorAttribute + " ")}{dto.ObjectType}" +
        $"{(dto.PersonName is null ? "" : $" \"{dto.PersonName}\"")}{(dto.PlateNumber is null ? "" : $" [{dto.PlateNumber}]")} " +
        $"({dto.Confidence:P0}) @ {dto.Timestamp:HH:mm:ss}";
}
