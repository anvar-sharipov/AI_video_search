using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace VMS.MetadataIndexer.Detection;

/// <summary>
/// Face detection + embedding for search-by-photo. Two separate models, on purpose:
///
/// - Detection: OpenCV's bundled Haar cascade (models/haarcascade_frontalface_default.xml)
///   via OpenCvSharp's CascadeClassifier. The modern YuNet detector would need a bare ONNX
///   Runtime session plus hand-written anchor decoding + NMS (OpenCvSharp4's Face module
///   only wraps the classic pre-2021 recognizers, not FaceDetectorYN — confirmed absent
///   from the installed OpenCvSharp4 build, not just undocumented) — the Haar cascade
///   gets a "find the face rectangle" result with zero custom decode logic.
///
/// - Alignment: a second Haar cascade (models/haarcascade_eye.xml) finds both eyes inside
///   the face box; the crop is rotated so the eye line is horizontal before resizing. This
///   is the classic 2-point alignment technique — cheaper than YuNet's 5-point landmarks,
///   but it directly fixes the dominant real-world source of embedding drift for a
///   front-facing CCTV camera (head tilt), which is exactly the gap the single-cascade
///   pipeline used to leave open. Falls back to an unaligned crop when both eyes can't be
///   found (side profile, sunglasses, low resolution) — a missed alignment must never turn
///   into a missed embedding.
///
/// - Embedding: SFace (models/face_recognition_sface_2021dec.onnx, opencv_zoo), run
///   directly through Microsoft.ML.OnnxRuntime. Preprocessing mirrors OpenCV's own
///   FaceRecognizerSF::feature(): resize the (now eye-aligned) face to 112x112, keep BGR
///   channel order, raw 0-255 pixel values (no per-channel mean/scale).
/// </summary>
public sealed class OnnxFaceEmbedder : IFaceEmbedder, IDisposable
{
    private const int EmbeddingInputSize = 112;

    // Below this many pixels across, the Haar box is too small for the embedding to be
    // trustworthy (a distant/low-res "person" detection) — better to return no embedding
    // than to feed the known-person matcher a near-random vector it might still score
    // above threshold against something by coincidence.
    private const int MinFaceSizePixels = 40;

    private readonly CascadeClassifier _detector;
    private readonly CascadeClassifier _eyeDetector;
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly ILogger _logger;

    public OnnxFaceEmbedder(string faceDetectorModelPath, string eyeDetectorModelPath, string faceEmbedderModelPath, ILogger logger)
    {
        _logger = logger;
        _detector = new CascadeClassifier(faceDetectorModelPath);
        _eyeDetector = new CascadeClassifier(eyeDetectorModelPath);
        _session = new InferenceSession(faceEmbedderModelPath);
        _inputName = _session.InputMetadata.Keys.First();
    }

    public Task<float[]?> TryGetEmbeddingAsync(string imagePath, DetectionBoundingBox? cropBox, CancellationToken ct = default)
    {
        using var image = Cv2.ImRead(imagePath);
        if (image.Empty())
        {
            _logger.LogWarning("Could not read image for face embedding: {Path}", imagePath);
            return Task.FromResult<float[]?>(null);
        }

        var ownsRegion = cropBox is not null;
        var region = cropBox is null ? image : new Mat(image, ClampToImage(cropBox, image));
        try
        {
            using var gray = new Mat();
            Cv2.CvtColor(region, gray, ColorConversionCodes.BGR2GRAY);

            var faces = _detector.DetectMultiScale(gray, scaleFactor: 1.1, minNeighbors: 5, minSize: new Size(30, 30));
            if (faces.Length == 0)
            {
                return Task.FromResult<float[]?>(null);
            }

            // "Most prominent face" = largest box, when more than one turns up in the crop/photo.
            var faceRect = faces.OrderByDescending(f => f.Width * f.Height).First();
            if (faceRect.Width < MinFaceSizePixels || faceRect.Height < MinFaceSizePixels)
            {
                return Task.FromResult<float[]?>(null);
            }

            using var aligned = AlignByEyes(region, gray, faceRect) ?? new Mat(region, faceRect);
            using var resized = new Mat();
            Cv2.Resize(aligned, resized, new Size(EmbeddingInputSize, EmbeddingInputSize));

            return Task.FromResult<float[]?>(Embed(resized));
        }
        finally
        {
            if (ownsRegion)
            {
                region.Dispose();
            }
        }
    }

    /// <summary>Best-effort 2-point alignment: finds both eyes inside the face box and rotates
    /// the face crop so the eye line is horizontal. Returns null (caller falls back to a plain
    /// crop) whenever fewer than two plausible eyes are found — a profile view, sunglasses, or
    /// just a cascade miss are all common and not errors.</summary>
    private Mat? AlignByEyes(Mat colorRegion, Mat grayRegion, Rect faceRect)
    {
        using var faceGray = new Mat(grayRegion, faceRect);
        var eyes = _eyeDetector.DetectMultiScale(faceGray, scaleFactor: 1.1, minNeighbors: 8,
            minSize: new Size(faceRect.Width / 8, faceRect.Height / 8));

        // Eyes sit in the upper half of a face; anything lower is a mouth/nostril false
        // positive from the cascade and would throw the tilt angle off.
        var upperHalf = eyes.Where(e => (e.Y + (e.Height / 2.0)) < faceRect.Height * 0.55).ToList();
        if (upperHalf.Count < 2)
        {
            return null;
        }

        // Largest two candidates, then left-to-right by center X, gives the actual eye pair
        // even when the cascade throws in a couple of smaller spurious boxes.
        var eyeCenters = upperHalf
            .OrderByDescending(e => e.Width * e.Height)
            .Take(2)
            .Select(e => new Point2f(faceRect.X + e.X + (e.Width / 2f), faceRect.Y + e.Y + (e.Height / 2f)))
            .OrderBy(p => p.X)
            .ToArray();

        var (leftEye, rightEye) = (eyeCenters[0], eyeCenters[1]);
        var angleDegrees = Math.Atan2(rightEye.Y - leftEye.Y, rightEye.X - leftEye.X) * 180.0 / Math.PI;

        // A cascade mismatch (e.g. one "eye" actually being a nostril) can produce a wild
        // angle — better to skip alignment than to rotate the face into nonsense.
        if (Math.Abs(angleDegrees) > 45)
        {
            return null;
        }

        var center = new Point2f((leftEye.X + rightEye.X) / 2f, (leftEye.Y + rightEye.Y) / 2f);
        using var rotationMatrix = Cv2.GetRotationMatrix2D(center, angleDegrees, 1.0);
        var rotated = new Mat();
        Cv2.WarpAffine(colorRegion, rotated, rotationMatrix, new Size(colorRegion.Width, colorRegion.Height),
            InterpolationFlags.Linear, BorderTypes.Replicate);

        var alignedFaceRect = ClampToImage(
            new DetectionBoundingBox(faceRect.X, faceRect.Y, faceRect.Width, faceRect.Height), rotated);
        var cropped = new Mat(rotated, alignedFaceRect);
        rotated.Dispose();
        return cropped;
    }

    private float[] Embed(Mat face112)
    {
        // NCHW, BGR (OpenCV's native channel order), raw 0-255 float — matches
        // cv::dnn::blobFromImage(face, 1.0, Size(112,112), Scalar(), swapRB=false).
        var tensor = new DenseTensor<float>([1, 3, EmbeddingInputSize, EmbeddingInputSize]);
        for (var y = 0; y < EmbeddingInputSize; y++)
        {
            for (var x = 0; x < EmbeddingInputSize; x++)
            {
                var pixel = face112.At<Vec3b>(y, x);
                tensor[0, 0, y, x] = pixel.Item0; // B
                tensor[0, 1, y, x] = pixel.Item1; // G
                tensor[0, 2, y, x] = pixel.Item2; // R
            }
        }

        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, tensor) };
        using var results = _session.Run(inputs);
        var embedding = results.First().AsEnumerable<float>().ToArray();

        if (embedding.Length != FaceEmbeddingDimensions.Value)
        {
            _logger.LogWarning(
                "SFace model returned a {Actual}-dim embedding, expected {Expected} — Elasticsearch's faceEmbedding mapping will reject this.",
                embedding.Length, FaceEmbeddingDimensions.Value);
        }

        return embedding;
    }

    private static Rect ClampToImage(DetectionBoundingBox box, Mat image)
    {
        var x = Math.Clamp(box.X, 0, image.Width - 1);
        var y = Math.Clamp(box.Y, 0, image.Height - 1);
        var width = Math.Clamp(box.Width, 1, image.Width - x);
        var height = Math.Clamp(box.Height, 1, image.Height - y);
        return new Rect(x, y, width, height);
    }

    public void Dispose()
    {
        _detector.Dispose();
        _eyeDetector.Dispose();
        _session.Dispose();
    }
}
