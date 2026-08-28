namespace VMS.MetadataIndexer.Search;

/// <summary>
/// Maps free-text search ("red car", "person") to structured filters. Deliberately
/// simple keyword matching, not NLP — the AI search box only ever needs to express
/// "object type" and "color", both closed vocabularies.
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
            }
        }

        return new SearchQuery(objectType, color, cameraId, from, to, size);
    }
}
