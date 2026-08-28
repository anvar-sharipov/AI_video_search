using Microsoft.Extensions.Logging;
using SkiaSharp;
using YoloDotNet;
using YoloDotNet.Enums;
using YoloDotNet.ExecutionProvider.Cpu;
using YoloDotNet.Models;
using YoloDotNet.Models.Interfaces;

namespace VMS.MetadataIndexer.Detection;

/// <summary>
/// Wraps YoloDotNet (ONNX Runtime under the hood) for offline object detection.
///
/// CPU execution provider only for now: YoloDotNet.ExecutionProvider.Cuda pulls in
/// Microsoft.ML.OnnxRuntime.Gpu.{Windows,Linux} (~200-400MB each), and that download
/// has repeatedly failed ("response ended prematurely") on this network. CPU is
/// plenty for a single test camera at a few fps; swap in CudaExecutionProvider here
/// once that package can actually be restored — nothing else in the pipeline needs
/// to change, IObjectDetector is the only contract callers depend on.
/// </summary>
public sealed class YoloDotNetDetector : IObjectDetector, IDisposable
{
    private readonly Yolo _yolo;
    private readonly IExecutionProvider _executionProvider;
    private readonly double _confidenceThreshold;

    public YoloDotNetDetector(string onnxModelPath, bool useCuda, double confidenceThreshold, ILogger logger)
    {
        if (useCuda)
        {
            logger.LogWarning("CUDA execution provider requested but not installed — falling back to CPU inference.");
        }

        _executionProvider = new CpuExecutionProvider(onnxModelPath);

        _yolo = new Yolo(new YoloOptions
        {
            ExecutionProvider = _executionProvider,
            ImageResize = ImageResize.Proportional
        });
        _confidenceThreshold = confidenceThreshold;
    }

    public Task<IReadOnlyList<DetectedObject>> DetectAsync(string imagePath, CancellationToken ct = default)
    {
        using var image = SKImage.FromEncodedData(imagePath);
        using var bitmap = SKBitmap.FromImage(image);

        var results = _yolo.RunObjectDetection(image, confidence: _confidenceThreshold, iou: 0.7);

        var detections = new List<DetectedObject>(results.Count);
        foreach (var r in results)
        {
            var box = new DetectionBoundingBox(r.BoundingBox.Left, r.BoundingBox.Top, r.BoundingBox.Width, r.BoundingBox.Height);
            var color = ColorTagger.AppliesTo(r.Label.Name) ? ColorTagger.TagColor(bitmap, box) : null;
            detections.Add(new DetectedObject(r.Label.Name, r.Confidence, box, color));
        }

        return Task.FromResult<IReadOnlyList<DetectedObject>>(detections);
    }

    public void Dispose()
    {
        _yolo.Dispose();
        _executionProvider.Dispose();
    }
}
