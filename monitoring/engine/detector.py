"""Model wrappers: YOLOv11 person and face detection, and InsightFace embeddings.

Models are loaded once per process and shared by all camera workers. Ultralytics
and ONNX sessions are not guaranteed to be thread-safe, so inference is serialized
with a lock. Scale out by running more engine replicas (see ENGINE_SHARD_COUNT),
not by adding threads to one process.
"""

from __future__ import annotations

import logging
import threading
import numpy as np

from .config import Settings

log = logging.getLogger(__name__)


COCO_PERSON_CLASS_ID = 0


class YoloDetector:
    """Wraps one YOLO model for a single class. Inference is serialized per model."""

    def __init__(self, weights: str, class_id: int, conf: float, imgsz: int, device: str, label: str) -> None:
        from ultralytics import YOLO  # imported lazily so pure modules and tests stay light

        self._model = YOLO(weights)
        self._class_id = class_id
        self._conf = conf
        self._imgsz = imgsz
        self._device = device
        self._lock = threading.Lock()
        log.info("Loaded %s weights from %s on %s", label, weights, device)

    def detect(self, frame: np.ndarray) -> np.ndarray:
        """Returns an (n, 5) array of x1, y1, x2, y2, confidence in pixel coordinates."""
        with self._lock:
            result = self._model.predict(
                frame,
                conf=self._conf,
                imgsz=self._imgsz,
                classes=[self._class_id],
                device=self._device,
                verbose=False,
            )[0]
        boxes = result.boxes
        if boxes is None or len(boxes) == 0:
            return np.zeros((0, 5), dtype=np.float32)
        xyxy = boxes.xyxy.cpu().numpy().astype(np.float32)
        conf = boxes.conf.cpu().numpy().astype(np.float32).reshape(-1, 1)
        return np.hstack([xyxy, conf])


class FaceDetector(YoloDetector):
    def __init__(self, settings: Settings) -> None:
        super().__init__(
            settings.yolo_weights,
            settings.face_class_id,
            settings.detect_confidence,
            settings.detect_imgsz,
            settings.inference_device,
            label="face",
        )


class PersonDetector(YoloDetector):
    def __init__(self, settings: Settings) -> None:
        super().__init__(
            settings.person_weights,
            COCO_PERSON_CLASS_ID,
            settings.person_confidence,
            settings.person_imgsz,
            settings.inference_device,
            label="person",
        )


class FaceRecognizer:
    """Produces a 512-d ArcFace embedding for the largest face inside a crop."""

    def __init__(self, settings: Settings) -> None:
        from insightface.app import FaceAnalysis

        providers = (
            ["CUDAExecutionProvider", "CPUExecutionProvider"]
            if settings.inference_device.startswith("cuda")
            else ["CPUExecutionProvider"]
        )
        det = settings.insightface_det_size
        self._app = FaceAnalysis(name=settings.insightface_model, providers=providers)
        self._app.prepare(ctx_id=0 if settings.inference_device.startswith("cuda") else -1, det_size=(det, det))
        self._lock = threading.Lock()

    def embed(self, crop_bgr: np.ndarray) -> np.ndarray | None:
        with self._lock:
            faces = self._app.get(crop_bgr)
        if not faces:
            return None
        largest = max(faces, key=lambda f: (f.bbox[2] - f.bbox[0]) * (f.bbox[3] - f.bbox[1]))
        return np.asarray(largest.normed_embedding, dtype=np.float32)
