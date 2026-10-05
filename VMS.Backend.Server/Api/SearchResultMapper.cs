using VMS.Core.Domain;

namespace VMS.Backend.Server.Api;

/// <summary>
/// Flattens an indexed ObjectMetadataEvent into the wire shape the WPF client's
/// SearchResultDto expects, adding an optional MatchScore — only ever a real
/// Elasticsearch kNN cosine-similarity score (face/reverse-image search), never
/// fabricated for plain attribute/text search (MatchScore stays null there).
/// </summary>
public static class SearchResultMapper
{
    public static object ToDto(ObjectMetadataEvent e, double? matchScore) => new
    {
        e.Id,
        e.CameraId,
        e.Timestamp,
        e.ObjectType,
        e.ColorAttribute,
        e.Confidence,
        e.BoundingBox,
        e.VideoChunkLocation,
        e.PersonName,
        e.PlateNumber,
        MatchScore = matchScore
    };
}
