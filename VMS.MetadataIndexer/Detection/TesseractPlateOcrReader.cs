using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Tesseract;
using Rect = OpenCvSharp.Rect;

namespace VMS.MetadataIndexer.Detection;

/// <summary>
/// Best-effort license-plate OCR. Honest limitation up front: this project has no dedicated
/// plate-detector model (the kind real ANPR systems train specifically to localize a plate
/// rectangle) — YOLO's vehicle detection only gives a box around the whole vehicle. This reader
/// approximates plate localization with a plain contour heuristic (look for a plate-shaped
/// rectangle — wide, short, roughly 2:1 to 6:1 — in the lower part of the vehicle box, since
/// that's where plates sit on a bumper) and falls back to just OCR'ing the bottom third of the
/// vehicle crop when no such contour turns up. Tesseract then reads whatever crop it's given.
/// Real-world accuracy will be materially lower than a proper ANPR pipeline — this is meant to
/// surface plausible reads, not guarantee correct ones; see the README's known limitations.
/// </summary>
public sealed class TesseractPlateOcrReader : IPlateOcrReader, IDisposable
{
    private const int MinAcceptedLength = 4;
    private const double MinAspectRatio = 2.0;
    private const double MaxAspectRatio = 6.0;

    private readonly TesseractEngine _engine;
    private readonly ILogger _logger;

    public TesseractPlateOcrReader(string tessDataPath, ILogger logger)
    {
        _logger = logger;
        _engine = new TesseractEngine(tessDataPath, "eng", EngineMode.Default);
        _engine.SetVariable("tessedit_char_whitelist", "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789");
        _engine.DefaultPageSegMode = PageSegMode.SingleLine;
    }

    public Task<string?> TryReadPlateAsync(string imagePath, DetectionBoundingBox vehicleBox, CancellationToken ct = default)
    {
        using var image = Cv2.ImRead(imagePath);
        if (image.Empty())
        {
            _logger.LogWarning("Could not read image for plate OCR: {Path}", imagePath);
            return Task.FromResult<string?>(null);
        }

        var rect = ClampToImage(vehicleBox, image);
        using var vehicleCrop = new Mat(image, rect);

        using var plateCrop = FindPlateRegion(vehicleCrop) ?? LowerThirdFallback(vehicleCrop);
        if (plateCrop is null || plateCrop.Empty())
        {
            return Task.FromResult<string?>(null);
        }

        return Task.FromResult(RunOcr(plateCrop));
    }

    /// <summary>Looks for a plate-shaped contour in the lower 60% of the vehicle crop — where a plate sits relative to the whole vehicle box in a typical front/rear view.</summary>
    private static Mat? FindPlateRegion(Mat vehicleCrop)
    {
        var bandY = (int)(vehicleCrop.Height * 0.4);
        if (bandY >= vehicleCrop.Height)
        {
            return null;
        }

        using var band = new Mat(vehicleCrop, new Rect(0, bandY, vehicleCrop.Width, vehicleCrop.Height - bandY));
        using var gray = new Mat();
        Cv2.CvtColor(band, gray, ColorConversionCodes.BGR2GRAY);
        using var thresh = new Mat();
        Cv2.Threshold(gray, thresh, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        Cv2.FindContours(thresh, out var contours, out _, RetrievalModes.List, ContourApproximationModes.ApproxSimple);

        Rect? best = null;
        var bestAspectDelta = double.MaxValue;
        const double idealAspect = 4.3;

        foreach (var contour in contours)
        {
            var box = Cv2.BoundingRect(contour);
            if (box.Width < band.Width * 0.2 || box.Width > band.Width * 0.95)
            {
                continue;
            }

            var aspect = (double)box.Width / Math.Max(box.Height, 1);
            if (aspect < MinAspectRatio || aspect > MaxAspectRatio)
            {
                continue;
            }

            var delta = Math.Abs(aspect - idealAspect);
            if (delta < bestAspectDelta)
            {
                bestAspectDelta = delta;
                best = box;
            }
        }

        if (best is not { } chosen)
        {
            return null;
        }

        // Map the candidate rect (found within `band`) back into vehicleCrop's coordinates.
        var full = new Rect(chosen.X, chosen.Y + bandY, chosen.Width, chosen.Height);
        return new Mat(vehicleCrop, full);
    }

    private static Mat LowerThirdFallback(Mat vehicleCrop)
    {
        var y0 = (int)(vehicleCrop.Height * 0.67);
        if (y0 >= vehicleCrop.Height)
        {
            y0 = Math.Max(vehicleCrop.Height - 1, 0);
        }

        return new Mat(vehicleCrop, new Rect(0, y0, vehicleCrop.Width, vehicleCrop.Height - y0));
    }

    private string? RunOcr(Mat plateCrop)
    {
        // Plates are usually small in a wide snapshot — upscale short crops so Tesseract has
        // enough pixel resolution per character to work with.
        using var scaled = new Mat();
        var scale = plateCrop.Height < 48 ? 48.0 / plateCrop.Height : 1.0;
        Cv2.Resize(plateCrop, scaled, new Size(), scale, scale, InterpolationFlags.Cubic);

        Cv2.ImEncode(".png", scaled, out var pngBytes);

        using var pix = Pix.LoadFromMemory(pngBytes);
        using var page = _engine.Process(pix);
        var raw = page.GetText();

        var normalized = new string(raw.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return normalized.Length >= MinAcceptedLength ? normalized : null;
    }

    private static Rect ClampToImage(DetectionBoundingBox box, Mat image)
    {
        var x = Math.Clamp(box.X, 0, image.Width - 1);
        var y = Math.Clamp(box.Y, 0, image.Height - 1);
        var width = Math.Clamp(box.Width, 1, image.Width - x);
        var height = Math.Clamp(box.Height, 1, image.Height - y);
        return new Rect(x, y, width, height);
    }

    public void Dispose() => _engine.Dispose();
}
