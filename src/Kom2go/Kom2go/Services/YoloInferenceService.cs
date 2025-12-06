using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Kom2go.Services;

/// <summary>
/// Service for running YOLO object detection inference using ONNX Runtime
/// </summary>
public class YoloInferenceService : IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _modelPath;
    private bool _disposed;
    
    // YOLO model input parameters
    private const int ModelInputWidth = 640;
    private const int ModelInputHeight = 640;
    private const float ConfidenceThreshold = 0.25f;
    private const float IouThreshold = 0.45f;
    
    // Model configuration - adjust based on your YOLO model
    // Common input names: "images" (YOLOv8/v10/v11), "input" (some models)
    private const string InputTensorName = "images";
    
    // Padding color for image preprocessing
    // Use gray for most comics, adjust if needed (white, black, etc.)
    private static readonly Color PaddingColor = Color.Gray;
    
    /// <summary>
    /// Initialize YOLO inference service with a model file
    /// </summary>
    /// <param name="modelPath">Path to the ONNX model file</param>
    public YoloInferenceService(string modelPath)
    {
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException($"YOLO model file not found at {modelPath}");
        }
        
        _modelPath = modelPath;
        
        try
        {
            var sessionOptions = new SessionOptions();
            // Use CPU for now, can be configured for GPU later
            _session = new InferenceSession(modelPath, sessionOptions);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load YOLO model from {modelPath}", ex);
        }
    }
    
    /// <summary>
    /// Run inference on an image and get bounding boxes
    /// </summary>
    /// <param name="imageData">Raw image data</param>
    /// <returns>List of detected bounding boxes with confidence scores</returns>
    public List<YoloDetection> DetectObjects(byte[] imageData)
    {
        using var image = Image.Load<Rgb24>(imageData);
        return DetectObjects(image);
    }
    
    /// <summary>
    /// Run inference on an image and get bounding boxes
    /// </summary>
    /// <param name="image">ImageSharp image</param>
    /// <returns>List of detected bounding boxes with confidence scores</returns>
    public List<YoloDetection> DetectObjects(Image<Rgb24> image)
    {
        // Store original dimensions
        var originalWidth = image.Width;
        var originalHeight = image.Height;
        
        // Preprocess image for YOLO model
        var inputTensor = PreprocessImage(image);
        
        // Create input container
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(InputTensorName, inputTensor)
        };
        
        // Run inference
        using var results = _session.Run(inputs);
        
        // Extract output tensor
        var outputTensor = results.First().AsTensor<float>();
        
        // Post-process results
        var detections = PostprocessOutput(outputTensor, originalWidth, originalHeight);
        
        // Apply Non-Maximum Suppression
        var filteredDetections = ApplyNMS(detections, IouThreshold);
        
        return filteredDetections;
    }
    
    /// <summary>
    /// Preprocess image for YOLO model input
    /// Resizes to model input size and normalizes pixel values
    /// </summary>
    private DenseTensor<float> PreprocessImage(Image<Rgb24> image)
    {
        // Resize image to model input size while maintaining aspect ratio
        var resized = image.Clone(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(ModelInputWidth, ModelInputHeight),
            Mode = ResizeMode.Pad,
            PadColor = PaddingColor
        }));
        
        // Create tensor with shape [1, 3, height, width]
        var tensor = new DenseTensor<float>(new[] { 1, 3, ModelInputHeight, ModelInputWidth });
        
        // Fill tensor with normalized pixel values
        resized.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < ModelInputHeight; y++)
            {
                var pixelRow = accessor.GetRowSpan(y);
                for (int x = 0; x < ModelInputWidth; x++)
                {
                    var pixel = pixelRow[x];
                    // Normalize to [0, 1] range
                    tensor[0, 0, y, x] = pixel.R / 255f;
                    tensor[0, 1, y, x] = pixel.G / 255f;
                    tensor[0, 2, y, x] = pixel.B / 255f;
                }
            }
        });
        
        return tensor;
    }
    
    /// <summary>
    /// Post-process YOLO model output to extract detections
    /// </summary>
    /// <remarks>
    /// This method supports two common YOLO output formats:
    /// 1. [1, num_predictions, features] - Standard format used by YOLOv8/v10/v11
    /// 2. [1, features, num_predictions] - Transposed format used by some models
    /// 
    /// Where features typically contains: [x_center, y_center, width, height, confidence, class_id]
    /// 
    /// If your model uses a different format, adjust the parsing logic accordingly.
    /// </remarks>
    private List<YoloDetection> PostprocessOutput(Tensor<float> output, int originalWidth, int originalHeight)
    {
        var detections = new List<YoloDetection>();
        
        var dimensions = output.Dimensions.ToArray();
        int numPredictions;
        int stride;
        
        // Determine output format based on dimension sizes
        // Note: This assumes the predictions dimension is larger than the features dimension
        if (dimensions[1] > dimensions[2])
        {
            // Format: [1, num_predictions, 6]
            numPredictions = dimensions[1];
            stride = dimensions[2];
            
            for (int i = 0; i < numPredictions; i++)
            {
                var confidence = output[0, i, 4];
                
                if (confidence < ConfidenceThreshold)
                    continue;
                
                var centerX = output[0, i, 0];
                var centerY = output[0, i, 1];
                var width = output[0, i, 2];
                var height = output[0, i, 3];
                var classId = stride > 5 ? (int)output[0, i, 5] : 0;
                
                // Convert from model coordinates to original image coordinates
                var x1 = (centerX - width / 2) * originalWidth / ModelInputWidth;
                var y1 = (centerY - height / 2) * originalHeight / ModelInputHeight;
                var x2 = (centerX + width / 2) * originalWidth / ModelInputWidth;
                var y2 = (centerY + height / 2) * originalHeight / ModelInputHeight;
                
                detections.Add(new YoloDetection
                {
                    X1 = Math.Max(0, x1),
                    Y1 = Math.Max(0, y1),
                    X2 = Math.Min(originalWidth, x2),
                    Y2 = Math.Min(originalHeight, y2),
                    Confidence = confidence,
                    ClassId = classId
                });
            }
        }
        else
        {
            // Format: [1, 6, num_predictions] - transposed format
            numPredictions = dimensions[2];
            stride = dimensions[1];
            
            for (int i = 0; i < numPredictions; i++)
            {
                var confidence = output[0, 4, i];
                
                if (confidence < ConfidenceThreshold)
                    continue;
                
                var centerX = output[0, 0, i];
                var centerY = output[0, 1, i];
                var width = output[0, 2, i];
                var height = output[0, 3, i];
                var classId = stride > 5 ? (int)output[0, 5, i] : 0;
                
                // Convert from model coordinates to original image coordinates
                var x1 = (centerX - width / 2) * originalWidth / ModelInputWidth;
                var y1 = (centerY - height / 2) * originalHeight / ModelInputHeight;
                var x2 = (centerX + width / 2) * originalWidth / ModelInputWidth;
                var y2 = (centerY + height / 2) * originalHeight / ModelInputHeight;
                
                detections.Add(new YoloDetection
                {
                    X1 = Math.Max(0, x1),
                    Y1 = Math.Max(0, y1),
                    X2 = Math.Min(originalWidth, x2),
                    Y2 = Math.Min(originalHeight, y2),
                    Confidence = confidence,
                    ClassId = classId
                });
            }
        }
        
        return detections;
    }
    
    /// <summary>
    /// Apply Non-Maximum Suppression to filter overlapping detections
    /// </summary>
    private List<YoloDetection> ApplyNMS(List<YoloDetection> detections, float iouThreshold)
    {
        if (detections.Count == 0)
            return detections;
        
        // Sort by confidence descending
        var sorted = detections.OrderByDescending(d => d.Confidence).ToList();
        var selected = new List<YoloDetection>();
        
        while (sorted.Count > 0)
        {
            var current = sorted[0];
            selected.Add(current);
            sorted.RemoveAt(0);
            
            // Remove boxes with high IoU
            sorted = sorted.Where(box => CalculateIoU(current, box) < iouThreshold).ToList();
        }
        
        return selected;
    }
    
    /// <summary>
    /// Calculate Intersection over Union (IoU) between two boxes
    /// </summary>
    private float CalculateIoU(YoloDetection box1, YoloDetection box2)
    {
        var x1 = Math.Max(box1.X1, box2.X1);
        var y1 = Math.Max(box1.Y1, box2.Y1);
        var x2 = Math.Min(box1.X2, box2.X2);
        var y2 = Math.Min(box1.Y2, box2.Y2);
        
        var intersectionArea = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
        var box1Area = (box1.X2 - box1.X1) * (box1.Y2 - box1.Y1);
        var box2Area = (box2.X2 - box2.X1) * (box2.Y2 - box2.Y1);
        var unionArea = box1Area + box2Area - intersectionArea;
        
        return unionArea > 0 ? intersectionArea / unionArea : 0;
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _session?.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Represents a detected object from YOLO inference
/// </summary>
public class YoloDetection
{
    /// <summary>
    /// X coordinate of top-left corner
    /// </summary>
    public float X1 { get; set; }
    
    /// <summary>
    /// Y coordinate of top-left corner
    /// </summary>
    public float Y1 { get; set; }
    
    /// <summary>
    /// X coordinate of bottom-right corner
    /// </summary>
    public float X2 { get; set; }
    
    /// <summary>
    /// Y coordinate of bottom-right corner
    /// </summary>
    public float Y2 { get; set; }
    
    /// <summary>
    /// Confidence score (0-1)
    /// </summary>
    public float Confidence { get; set; }
    
    /// <summary>
    /// Detected class ID
    /// </summary>
    public int ClassId { get; set; }
    
    /// <summary>
    /// Width of the bounding box
    /// </summary>
    public float Width => X2 - X1;
    
    /// <summary>
    /// Height of the bounding box
    /// </summary>
    public float Height => Y2 - Y1;
    
    /// <summary>
    /// Center X coordinate
    /// </summary>
    public float CenterX => (X1 + X2) / 2;
    
    /// <summary>
    /// Center Y coordinate
    /// </summary>
    public float CenterY => (Y1 + Y2) / 2;
}
