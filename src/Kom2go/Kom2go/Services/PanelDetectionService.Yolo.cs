using Kom2go.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Kom2go.Services;

/// <summary>
/// YOLO-based panel detection implementation
/// </summary>
public partial class PanelDetectionService
{
    private YoloInferenceService? _yoloService;
    private readonly string _yoloModelPath;
    private readonly bool _useYolo;
    
    /// <summary>
    /// Initialize panel detection service with optional YOLO model
    /// </summary>
    /// <param name="yoloModelPath">Path to YOLO ONNX model file. If null or file doesn't exist, falls back to traditional algorithm</param>
    public PanelDetectionService(string? yoloModelPath = null)
    {
        _yoloModelPath = yoloModelPath ?? GetDefaultModelPath();
        _useYolo = !string.IsNullOrEmpty(_yoloModelPath) && File.Exists(_yoloModelPath);
        
        if (_useYolo)
        {
            try
            {
                _yoloService = new YoloInferenceService(_yoloModelPath);
            }
            catch (Exception ex)
            {
                // Fall back to traditional algorithm if YOLO initialization fails
                System.Diagnostics.Debug.WriteLine($"Failed to initialize YOLO service: {ex.Message}");
                _useYolo = false;
                _yoloService = null;
            }
        }
    }
    
    /// <summary>
    /// Get default model path from application directory
    /// </summary>
    private static string GetDefaultModelPath()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(appDir, "Models", "panel_detection.onnx");
    }
    
    /// <summary>
    /// Detect panels using YOLO model
    /// </summary>
    private PagePanelInfo DetectPanelsWithYolo(int pageIndex, byte[] pageData)
    {
        var result = new PagePanelInfo
        {
            PageIndex = pageIndex
        };
        
        try
        {
            if (_yoloService == null)
            {
                // Fall back to traditional algorithm
                return DetectPanelsInternal(pageIndex, pageData);
            }
            
            using var image = Image.Load<Rgb24>(pageData);
            var imageWidth = image.Width;
            var imageHeight = image.Height;
            
            // Run YOLO inference
            var detections = _yoloService.DetectObjects(image);
            
            if (detections.Count == 0)
            {
                // No panels detected - treat as splash page
                result.IsSplashPage = true;
                result.DetectionSuccessful = true;
                result.Panels.Add(CreateFullPagePanel(pageIndex));
                return result;
            }
            
            // Convert YOLO detections to ComicPanel objects
            var panels = new List<ComicPanel>();
            
            // Sort panels in reading order (top to bottom, left to right)
            var sortedDetections = detections
                .OrderBy(d => d.CenterY)
                .ThenBy(d => d.CenterX)
                .ToList();
            
            for (int i = 0; i < sortedDetections.Count; i++)
            {
                var detection = sortedDetections[i];
                
                // Skip very small detections
                if (detection.Width < imageWidth * MinPanelSizeRatio ||
                    detection.Height < imageHeight * MinPanelSizeRatio)
                {
                    continue;
                }
                
                // Convert from pixel coordinates to normalized coordinates
                panels.Add(new ComicPanel
                {
                    PageIndex = pageIndex,
                    PanelIndex = i,
                    X = detection.X1 / imageWidth,
                    Y = detection.Y1 / imageHeight,
                    Width = detection.Width / imageWidth,
                    Height = detection.Height / imageHeight,
                    Confidence = detection.Confidence
                });
            }
            
            if (panels.Count > 0)
            {
                result.Panels = panels;
                result.DetectionSuccessful = true;
                result.IsSplashPage = panels.Count == 1;
            }
            else
            {
                // No valid panels - treat as splash page
                result.IsSplashPage = true;
                result.DetectionSuccessful = true;
                result.Panels.Add(CreateFullPagePanel(pageIndex));
            }
        }
        catch (Exception ex)
        {
            // On error, treat page as splash page
            System.Diagnostics.Debug.WriteLine($"YOLO panel detection failed for page {pageIndex}: {ex.Message}");
            result.DetectionSuccessful = false;
            result.IsSplashPage = true;
            result.Panels.Add(CreateFullPagePanel(pageIndex, 0.5));
        }
        
        return result;
    }
    
    /// <summary>
    /// Create a full-page panel
    /// </summary>
    private static ComicPanel CreateFullPagePanel(int pageIndex, double confidence = 1.0)
    {
        return new ComicPanel
        {
            PageIndex = pageIndex,
            PanelIndex = 0,
            X = 0,
            Y = 0,
            Width = 1,
            Height = 1,
            Confidence = confidence
        };
    }
    
    /// <summary>
    /// Dispose YOLO service
    /// </summary>
    public void Dispose()
    {
        _yoloService?.Dispose();
    }
}
