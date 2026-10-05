using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VMS.Core.Data;

namespace VMS.MetadataIndexer.Detection;

/// <summary>
/// Caches enrolled KnownPerson embeddings in memory and compares a new detection's embedding
/// against all of them via cosine similarity — a plain in-memory scan rather than a second
/// Elasticsearch kNN index, since the known-persons gallery is expected to stay small (tens to
/// low hundreds of entries, enrolled by hand) and this runs once per "person" detection across
/// every camera's DetectionWorker. 0.363 is OpenCV's own published "same identity" threshold
/// for the SFace model this project already uses (see OnnxFaceEmbedder) — reused here rather
/// than inventing a new one.
///
/// A name can have more than one enrolled row (KnownPersonEndpoints lets an operator enroll the
/// same name again with another reference photo — there's no dedicated "add a second photo" UI,
/// just re-enrolling, but the effect is the same): matching takes the BEST-scoring row per name,
/// so a person recognized well from any one of several reference angles still counts as a match.
/// </summary>
public sealed class KnownPersonMatcher(IDbContextFactory<VmsDbContext> dbContextFactory, ILogger<KnownPersonMatcher> logger) : IKnownPersonMatcher
{
    private const float SimilarityThreshold = 0.363f;

    // The winning name's best score must clear the runner-up's best score by at least this
    // much, not just the absolute threshold above — otherwise two enrolled people who happen
    // to look similar can flip a match between them on essentially a coin toss whenever both
    // scores land close together just above SimilarityThreshold.
    private const float MinConfidenceMargin = 0.05f;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<(string Name, float[] Embedding)>? _cache;

    public void Invalidate() => _cache = null;

    public async Task<string?> TryMatchAsync(float[] embedding, CancellationToken ct = default)
    {
        var cache = await GetCacheAsync(ct);
        if (cache.Count == 0)
        {
            return null;
        }

        var bestScoreByName = new Dictionary<string, float>();
        foreach (var (name, knownEmbedding) in cache)
        {
            var score = CosineSimilarity(embedding, knownEmbedding);
            if (!bestScoreByName.TryGetValue(name, out var existingBest) || score > existingBest)
            {
                bestScoreByName[name] = score;
            }
        }

        var ranked = bestScoreByName.OrderByDescending(kv => kv.Value).ToList();
        if (ranked[0].Value < SimilarityThreshold)
        {
            return null;
        }

        if (ranked.Count > 1 && (ranked[0].Value - ranked[1].Value) < MinConfidenceMargin)
        {
            return null;
        }

        return ranked[0].Key;
    }

    private async Task<List<(string Name, float[] Embedding)>> GetCacheAsync(CancellationToken ct)
    {
        if (_cache is { } existing)
        {
            return existing;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (_cache is { } cachedWhileWaiting)
            {
                return cachedWhileWaiting;
            }

            await using var db = await dbContextFactory.CreateDbContextAsync(ct);
            var people = await db.KnownPersons.AsNoTracking().ToListAsync(ct);
            _cache = people.Select(p => (p.Name, p.FaceEmbedding)).ToList();
            logger.LogInformation("Loaded {Count} known persons for face matching.", _cache.Count);
            return _cache;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
        {
            return 0f;
        }

        float dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denominator == 0 ? 0f : dot / denominator;
    }
}
