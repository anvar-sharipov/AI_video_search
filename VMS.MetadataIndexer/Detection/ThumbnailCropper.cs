using SkiaSharp;
using VMS.Core.Domain;

namespace VMS.MetadataIndexer.Detection;

/// <summary>
/// Crops a still frame down to one detection's bounding box for the search-results grid's
/// thumbnail cards — reuses SkiaSharp (already a dependency via ColorTagger) rather than
/// adding a second image library just for the backend host project.
/// </summary>
public static class ThumbnailCropper
{
    public static byte[] CropToBoundingBox(string imagePath, BoundingBox box, double paddingFraction = 0.15)
    {
        using var bitmap = SKBitmap.Decode(imagePath) ?? throw new InvalidOperationException($"Could not decode image '{imagePath}'.");

        // A tight detection box crops out useful context (rest of a vehicle, a person's
        // whole silhouette) — pad on every side, then clamp to the frame so the card still
        // renders something sensible for boxes near an edge.
        var padX = box.Width * paddingFraction;
        var padY = box.Height * paddingFraction;

        var x0 = (int)Math.Clamp(box.X - padX, 0, bitmap.Width - 1);
        var y0 = (int)Math.Clamp(box.Y - padY, 0, bitmap.Height - 1);
        var x1 = (int)Math.Clamp(box.X + box.Width + padX, x0 + 1, bitmap.Width);
        var y1 = (int)Math.Clamp(box.Y + box.Height + padY, y0 + 1, bitmap.Height);

        using var cropped = new SKBitmap(x1 - x0, y1 - y0);
        using (var canvas = new SKCanvas(cropped))
        {
            canvas.DrawBitmap(bitmap, new SKRect(x0, y0, x1, y1), new SKRect(0, 0, x1 - x0, y1 - y0));
        }

        using var image = SKImage.FromBitmap(cropped);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
        return data.ToArray();
    }

    /// <summary>Pixel dimensions of an image file — used to normalize a detection's absolute-pixel
    /// BoundingBox into fractional (0..1) coordinates for a consumer (e.g. a live video overlay)
    /// that only knows the video control's own on-screen size, not the source frame's resolution.</summary>
    public static (int Width, int Height) ReadDimensions(string imagePath)
    {
        var info = SKBitmap.DecodeBounds(imagePath);
        return (info.Width, info.Height);
    }
}
