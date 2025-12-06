# YOLO Panel Detection Model

This directory should contain the YOLO ONNX model file for comic panel detection.

## Model Setup

### Option 1: Use a Pre-trained YOLO Model

If you have a YOLOv10, YOLOv11, or similar YOLO model already trained for comic panel detection:

1. Export your model to ONNX format:
   ```bash
   # For YOLOv8/YOLOv10/YOLOv11 (using Ultralytics)
   from ultralytics import YOLO
   model = YOLO('path/to/your/model.pt')
   model.export(format='onnx')
   ```

2. Place the exported ONNX file in this directory and name it `panel_detection.onnx`

### Option 2: Train Your Own Model

To train a custom YOLO model for comic panel detection:

1. **Prepare Dataset**:
   - Collect comic book pages
   - Annotate panels using tools like LabelImg, CVAT, or Roboflow
   - Export annotations in YOLO format

2. **Train the Model**:
   ```python
   from ultralytics import YOLO
   
   # Load a pretrained model (recommended to start with YOLOv8n or YOLOv11n)
   model = YOLO('yolov8n.pt')  # or 'yolov11n.pt'
   
   # Train the model
   results = model.train(
       data='path/to/data.yaml',
       epochs=100,
       imgsz=640,
       batch=16,
       name='comic_panel_detector'
   )
   ```

3. **Export to ONNX**:
   ```python
   # Export the trained model
   model = YOLO('runs/detect/comic_panel_detector/weights/best.pt')
   model.export(format='onnx', opset=12)
   ```

4. Copy the exported ONNX file to this directory as `panel_detection.onnx`

### Option 3: Use Existing Object Detection Models

If you don't have a custom-trained model, you can use general object detection models as a starting point, though results may not be optimal:

1. Download a pre-trained YOLOv8 or YOLOv11 model from Ultralytics
2. Export it to ONNX format
3. Place it in this directory

**Note**: General object detection models are not trained on comic panels and will likely not work well. Custom training is strongly recommended for best results.

## Model File Location

The application expects the model file at one of these locations:

1. `Assets/Models/panel_detection.onnx` (relative to application directory)
2. Custom path can be specified when initializing `PanelDetectionService`

## Fallback Behavior

If no YOLO model is found, the application will automatically fall back to the traditional image processing algorithm for panel detection. This ensures the application continues to work even without a YOLO model.

## Model Input/Output Specification

The YOLO inference service expects:

- **Input**: RGB image resized to 640x640 with padding
- **Output**: Bounding boxes in format `[x1, y1, x2, y2, confidence, class_id]`
- **Class ID**: For single-class detection (panels), class_id should be 0

## Performance Tips

- Use GPU acceleration by installing CUDA-enabled ONNX Runtime: `Microsoft.ML.OnnxRuntime.Gpu`
- Smaller models (YOLOv8n, YOLOv11n) are faster but less accurate
- Larger models (YOLOv8x, YOLOv11x) are more accurate but slower
- Consider model quantization for faster inference on CPU

## Troubleshooting

### Model Loading Fails
- Verify the ONNX file is not corrupted
- Check that the file has read permissions
- Ensure ONNX Runtime is properly installed

### Poor Detection Results
- The model may not be trained on comic panels
- Try adjusting confidence threshold in `YoloInferenceService.cs`
- Consider training a custom model with your specific comic style

### Performance Issues
- Reduce model size (use YOLOv8n instead of YOLOv8x)
- Enable GPU acceleration if available
- Consider caching detection results (already implemented in PanelDetectionService)

## Resources

- [Ultralytics YOLOv8 Documentation](https://docs.ultralytics.com/)
- [ONNX Runtime Documentation](https://onnxruntime.ai/)
- [Roboflow for Dataset Creation](https://roboflow.com/)
- [LabelImg for Annotation](https://github.com/heartexlabs/labelImg)
