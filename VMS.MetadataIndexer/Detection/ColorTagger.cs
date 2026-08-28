using SkiaSharp;

namespace VMS.MetadataIndexer.Detection;

/// <summary>
/// COCO detection classes have no color attribute, so "red car" style search needs
/// one added ourselves: average the pixel color inside a detection's bounding box
/// and snap it to the nearest named color. Applied to vehicle-ish classes only —
/// color is meaningless noise for "person".
/// </summary>
public static class ColorTagger
{
    private static readonly HashSet<string> ColorTaggedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "car", "truck", "bus", "motorcycle", "bicycle"
    };

    private static readonly (string Name, byte R, byte G, byte B)[] NamedColors =
    [
        ("black", 0, 0, 0),
        ("white", 255, 255, 255),
        ("gray", 128, 128, 128),
        ("red", 200, 30, 30),
        ("orange", 230, 130, 30),
        ("yellow", 220, 210, 40),
        ("green", 40, 140, 60),
        ("blue", 40, 70, 180),
        ("silver", 190, 190, 190)
    ];

    public static bool AppliesTo(string objectType) => ColorTaggedTypes.Contains(objectType);

    public static string? TagColor(SKBitmap bitmap, DetectionBoundingBox box)
    {
        var x0 = Math.Clamp(box.X, 0, bitmap.Width - 1);
        var y0 = Math.Clamp(box.Y, 0, bitmap.Height - 1);
        var x1 = Math.Clamp(box.X + box.Width, x0 + 1, bitmap.Width);
        var y1 = Math.Clamp(box.Y + box.Height, y0 + 1, bitmap.Height);

        long sumR = 0, sumG = 0, sumB = 0;
        var count = 0;

        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                sumR += pixel.Red;
                sumG += pixel.Green;
                sumB += pixel.Blue;
                count++;
            }
        }

        if (count == 0)
        {
            return null;
        }

        var avgR = (byte)(sumR / count);
        var avgG = (byte)(sumG / count);
        var avgB = (byte)(sumB / count);

        return NamedColors
            .OrderBy(c => DistanceSquared(c.R, c.G, c.B, avgR, avgG, avgB))
            .First().Name;
    }

    private static int DistanceSquared(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2)
    {
        var dr = r1 - r2;
        var dg = g1 - g2;
        var db = b1 - b2;
        return dr * dr + dg * dg + db * db;
    }
}
