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
///   only wraps the classic pre-2021 recognizers, not FaceDetectorYN) — the Haar cascade
///   gets a "find the face rectangle" result with zero custom decode logic.
///
/// - Embedding: SFace (models/face_recognition_sface_2021dec.onnx, opencv_zoo), run
///   directly through Microsoft.ML.OnnxRuntime. Preprocessing mirrors OpenCV's own
///   FaceRecognizerSF::feature(): resize the cropped face to 112x112, keep BGR channel
///   order, raw 0-255 pixel values (no per-channel mean/scale) — no 5-point-landmark
///   alignment step (YuNet would normally supply the landmarks for that; the Haar
///   cascade only gives a bounding box), so embeddings are somewhat less precise for
///   off-angle faces than the full YuNet+SFace pipeline. Documented as a known limitation.
/// </summary>
public sealed class OnnxFaceEmbedder : IFaceEmbedder, IDisposable
{
    private const int EmbeddingInputSize = 112;

    private readonly CascadeClassifier _detector;
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly ILogger _logger;

    public OnnxFaceEmbedder(string faceDetectorModelPath, string faceEmbedderModelPath, ILogger logger)
    {
        _logger = logger;
        _detector = new CascadeClassifier(faceDetectorModelPath);
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

            using var face = new Mat(region, faceRect);
            using var resized = new Mat();
            Cv2.Resize(face, resized, new Size(EmbeddingInputSize, EmbeddingInputSize));

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
        _session.Dispose();
    }
}
