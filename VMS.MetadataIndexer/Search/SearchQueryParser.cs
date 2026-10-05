namespace VMS.MetadataIndexer.Search;

/// <summary>
/// Maps free-text search ("red car", "person", "Alice", "34ABC777") to structured filters.
/// Deliberately simple keyword matching, not NLP: "object type" and "color" are closed
/// vocabularies matched by lookup; anything left over is passed through as FreeTextQuery for
/// ElasticsearchIndexer to match against personName/plateNumber, since names and plates are
/// open vocabularies with nothing to look up here.
/// </summary>
public static class SearchQueryParser
{
    private static readonly HashSet<string> KnownColors = new(StringComparer.OrdinalIgnoreCase)
    {
        "black", "white", "gray", "grey", "red", "orange", "yellow", "green", "blue", "silver"
    };

    // COCO has no "face" class; the closest available signal is "person".
    // Documented limitation until a face-specific model is added.
    private static readonly Dictionary<string, string> ObjectTypeSynonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["car"] = "car",
        ["cars"] = "car",
        ["person"] = "person",
        ["people"] = "person",
        ["human"] = "person",
        ["face"] = "person",
        ["truck"] = "truck",
        ["bus"] = "bus",
        ["motorcycle"] = "motorcycle",
        ["motorbike"] = "motorcycle",
        ["bike"] = "bicycle",
        ["bicycle"] = "bicycle"
    };

    public static SearchQuery Parse(string freeText, string? cameraId = null, DateTimeOffset? from = null, DateTimeOffset? to = null, int size = 50)
    {
        string? objectType = null;
        string? color = null;
        var leftover = new List<string>();

        foreach (var token in freeText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (color is null && KnownColors.Contains(token))
            {
                color = token.Equals("grey", StringComparison.OrdinalIgnoreCase) ? "gray" : token.ToLowerInvariant();
                continue;
            }

            if (objectType is null && ObjectTypeSynonyms.TryGetValue(token, out var mapped))
            {
                objectType = mapped;
                continue;
            }

            leftover.Add(token);
        }

        // A leftover token might be a person's name or a plate number — both open vocabularies,
        // so unlike color/object-type there's nothing to map here; ElasticsearchIndexer matches
        // it against personName/plateNumber directly.
        var freeTextQuery = leftover.Count > 0 ? string.Join(' ', leftover) : null;

        return new SearchQuery(objectType, color, cameraId, from, to, size, freeTextQuery);
    }
}
