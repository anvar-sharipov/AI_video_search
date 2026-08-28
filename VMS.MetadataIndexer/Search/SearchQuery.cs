namespace VMS.MetadataIndexer.Search;

public record SearchQuery(
    string? ObjectType = null,
    string? ColorAttribute = null,
    string? CameraId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Size = 50);
