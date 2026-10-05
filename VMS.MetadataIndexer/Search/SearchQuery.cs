namespace VMS.MetadataIndexer.Search;

// FreeTextQuery: leftover tokens that matched neither a known color nor an object-type synonym —
// treated as a possible person name or plate number (matched against either field), since both
// are open vocabularies SearchQueryParser can't enumerate like colors/object types.
public record SearchQuery(
    string? ObjectType = null,
    string? ColorAttribute = null,
    string? CameraId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Size = 50,
    string? FreeTextQuery = null,
    // Exact match on a known, enrolled person's name — distinct from FreeTextQuery's
    // fuzzy should-match against either personName or plateNumber, for the Named Person
    // search tab where the caller picked a specific enrolled name from a list.
    string? PersonName = null);
