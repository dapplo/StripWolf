# YOLO Panel Detection Implementation Notes

## Overview

This document describes the implementation of YOLO-based machine learning model support for comic panel detection in Kom2go.

## What Was Implemented

### 1. ML.NET and ONNX Runtime Integration

Added support for running ONNX models using Microsoft ML.NET:
- **Package Added**: `Microsoft.ML.OnnxRuntime` (v1.20.1)
- **Purpose**: Enable loading and running YOLO object detection models for panel detection

### 2. YoloInferenceService (New)

A new service class that handles all YOLO model inference operations:

**Location**: `src/Kom2go/Kom2go/Services/YoloInferenceService.cs`

**Features**:
- Loads YOLO ONNX models from disk
- Pre-processes images using ImageSharp:
  - Resizes images to 640x640 (standard YOLO input size)
  - Maintains aspect ratio with padding
  - Normalizes pixel values to [0, 1] range
  - Converts to RGB tensor format
- Runs inference using ONNX Runtime
- Post-processes model output:
  - Extracts bounding boxes from YOLO predictions
  - Converts coordinates from model space to original image space
  - Applies Non-Maximum Suppression (NMS) to remove overlapping detections
- Supports multiple YOLO output formats (standard and transposed)
- Configurable parameters:
  - Input tensor name (default: "images")
  - Padding color (default: Gray)
  - Confidence threshold (default: 0.25)
  - IoU threshold for NMS (default: 0.45)

### 3. PanelDetectionService Updates

Modified the existing panel detection service to support YOLO:

**Files**:
- `src/Kom2go/Kom2go/Services/PanelDetectionService.cs` - Made partial, added IDisposable
- `src/Kom2go/Kom2go/Services/PanelDetectionService.Yolo.cs` - New partial class with YOLO logic

**Key Changes**:
- Made `PanelDetectionService` a partial class
- Added optional YOLO model path constructor parameter
- Automatic fallback to traditional algorithm if:
  - No model file is provided
  - Model file doesn't exist
  - Model loading fails
  - Inference fails
- Maintains full backward compatibility
- Implements `IDisposable` for proper cleanup
- Converts YOLO detections to existing `ComicPanel` format
- Preserves all existing caching functionality
- Adds debug logging for troubleshooting

**Detection Flow**:
1. Check if YOLO model is available and loaded
2. If yes: Use `DetectPanelsWithYolo()` method
3. If no: Use traditional `DetectPanelsInternal()` method
4. Convert results to normalized `ComicPanel` objects
5. Cache results for performance

### 4. Dependency Injection Configuration

Updated service registration to support optional constructor parameter:

**File**: `src/Kom2go/Kom2go/App.axaml.cs`

**Change**:
```csharp
services.AddSingleton<PanelDetectionService>(provider => new PanelDetectionService());
```

This allows easy future enhancement to pass model path from configuration.

### 5. Documentation

**Main README** (`README.md`):
- Added AI-Powered Panel Detection to features list
- Added comprehensive Panel Detection section explaining:
  - YOLO-based detection (recommended)
  - Traditional algorithm (fallback)
  - Setup instructions

**Model Setup Guide** (`src/Kom2go/Kom2go/Assets/Models/README.md`):
- Detailed instructions for obtaining YOLO models
- Three options explained:
  1. Use pre-trained model
  2. Train custom model
  3. Use general object detection model
- Export to ONNX instructions
- Troubleshooting guide
- Performance tips
- Model input/output specifications

### 6. Git Configuration

Updated `.gitignore` to exclude large model files:
- `*.onnx` - ONNX model files
- `*.pt` - PyTorch model files
- `*.weights` - YOLO weights files

## Architecture Decisions

### Why Partial Classes?

Used partial classes to separate YOLO logic from traditional algorithm:
- Keeps existing code clean and readable
- Makes YOLO support optional and modular
- Easy to maintain and extend separately
- Clear separation of concerns

### Why Optional Model?

Made YOLO model optional to ensure:
- App works out of the box without additional setup
- No breaking changes to existing functionality
- Users can choose their preferred detection method
- Graceful degradation if model is unavailable

### Why ONNX Runtime?

Chose ONNX Runtime over other ML frameworks because:
- Cross-platform support (Windows, Linux, macOS, Android)
- Good performance on CPU and GPU
- Wide model compatibility
- Active development and support
- Part of Microsoft's ML.NET ecosystem

### Why ImageSharp for Preprocessing?

Already used in the project for image processing:
- No additional dependencies needed
- Consistent image handling throughout codebase
- Good performance
- Rich API for image transformations

## Technical Details

### YOLO Output Format Support

The implementation supports two common YOLO output formats:

1. **Standard Format**: `[batch, num_predictions, features]`
   - Used by YOLOv8, YOLOv10, YOLOv11
   - Features: [x_center, y_center, width, height, confidence, class_id]

2. **Transposed Format**: `[batch, features, num_predictions]`
   - Used by some exported models
   - Same feature order, different tensor layout

The code automatically detects the format by comparing dimension sizes.

### Coordinate System

- **YOLO Model**: Outputs center-based coordinates (x_center, y_center, width, height) in model space (640x640)
- **Conversion**: Transforms to corner-based coordinates (x1, y1, x2, y2) in original image space
- **Normalization**: Final ComicPanel objects use normalized coordinates (0-1 range)

### Non-Maximum Suppression (NMS)

Implemented to filter overlapping detections:
- Sorts detections by confidence (descending)
- Keeps highest confidence detection
- Removes overlaps based on IoU threshold (default: 0.45)
- Ensures each panel is detected only once

## Usage

### Basic Usage (Default)

```csharp
// Uses default model path: Assets/Models/panel_detection.onnx
var service = new PanelDetectionService();
var result = await service.DetectPanelsAsync(comicPath, pageIndex, imageData);
```

### Custom Model Path

```csharp
// Use custom model location
var service = new PanelDetectionService("/path/to/custom_model.onnx");
var result = await service.DetectPanelsAsync(comicPath, pageIndex, imageData);
```

### Fallback Behavior

If model is not found or fails, automatically uses traditional algorithm:
```csharp
var service = new PanelDetectionService("non_existent.onnx");
// Still works! Falls back to traditional algorithm
var result = await service.DetectPanelsAsync(comicPath, pageIndex, imageData);
```

## Performance Considerations

### Memory Usage
- Model size: Varies by YOLO version (YOLOv8n: ~6MB, YOLOv8x: ~136MB)
- Model is loaded once and reused for all detections
- Tensors are created/disposed per detection
- Image preprocessing creates temporary image copy

### Speed
- CPU inference: ~50-500ms per image (depends on model size and CPU)
- GPU inference: ~10-50ms per image (requires GPU-enabled ONNX Runtime)
- Results are cached per page for instant repeated access

### Optimization Tips
1. Use smaller models (YOLOv8n/v11n) for faster inference
2. Enable GPU support by installing `Microsoft.ML.OnnxRuntime.Gpu`
3. Pre-detect panels in background for upcoming pages
4. Leverage existing caching infrastructure

## Future Enhancements

Possible improvements for future versions:

1. **Configuration System**:
   - Allow users to select model from settings
   - Adjust confidence/IoU thresholds via UI
   - Toggle between YOLO and traditional algorithm

2. **Model Management**:
   - Download models from cloud storage
   - Model version management
   - Auto-update models

3. **GPU Acceleration**:
   - Add GPU support via `Microsoft.ML.OnnxRuntime.Gpu`
   - Automatic GPU detection and usage
   - Fallback to CPU if GPU unavailable

4. **Training Support**:
   - Export training data from user corrections
   - Submit to community model training
   - Personalized models per user

5. **Batch Processing**:
   - Process multiple pages in parallel
   - Optimize memory usage for batch operations

6. **Model Formats**:
   - Support TensorFlow Lite for mobile
   - Support CoreML for iOS
   - Direct PyTorch model support

## Testing

Since no test infrastructure exists in the project, testing should be done manually:

1. **Without Model**:
   - Remove/rename model file
   - Verify app starts successfully
   - Verify guided reading mode works (traditional algorithm)
   - Check that panels are detected reasonably

2. **With Model**:
   - Place YOLO model at correct location
   - Verify app starts successfully
   - Verify guided reading mode works (YOLO)
   - Compare detection quality vs traditional algorithm
   - Check performance (speed, accuracy)

3. **Error Handling**:
   - Test with corrupted model file
   - Test with wrong model format
   - Verify graceful fallback to traditional algorithm
   - Check debug output for error messages

## Troubleshooting

### Model Not Loading

**Check**:
1. Model file exists at `Assets/Models/panel_detection.onnx`
2. File is valid ONNX format
3. File has read permissions
4. Check debug output for error messages

**Solution**: App will automatically fall back to traditional algorithm

### Poor Detection Quality

**Possible Causes**:
1. Model not trained on comic panels
2. Model trained on different comic style
3. Confidence threshold too high/low

**Solutions**:
1. Train custom model on your comic style
2. Adjust confidence threshold in `YoloInferenceService.cs`
3. Try different YOLO versions

### Slow Performance

**Solutions**:
1. Use smaller model (YOLOv8n instead of YOLOv8x)
2. Install GPU-enabled ONNX Runtime
3. Reduce image resolution (modify `ModelInputWidth/Height`)
4. Enable aggressive caching

## Security Considerations

- ✅ No vulnerabilities found in dependencies (checked via GitHub Advisory Database)
- ✅ No security issues found in code (checked via CodeQL)
- ✅ Model files excluded from git to prevent large file issues
- ✅ Proper exception handling prevents crashes
- ✅ No user input directly used in model paths (could add validation)

## Conclusion

This implementation provides a solid foundation for YOLO-based panel detection while maintaining full backward compatibility. The modular design allows easy enhancement and experimentation with different models and approaches.

The key achievement is enabling advanced ML-based detection without disrupting existing functionality or requiring immediate user action.
