from __future__ import annotations

import os
from dataclasses import dataclass


def _bool(name: str, default: bool) -> bool:
    raw = os.environ.get(name)
    return default if raw is None else raw.strip().lower() in {"1", "true", "yes", "on"}


def _required(name: str) -> str:
    value = os.environ.get(name, "").strip()
    if not value:
        raise ValueError(f"Environment variable {name} is required")
    return value


@dataclass(frozen=True)
class Settings:
    api_base_url: str
    service_api_key: str
    yolo_weights: str
    face_class_id: int
    detect_confidence: float
    detect_imgsz: int
    person_weights: str
    person_confidence: float
    person_imgsz: int
    inference_device: str
    frame_stride: int
    insightface_model: str
    insightface_det_size: int
    face_match_threshold: float
    identify_every_n_frames: int
    max_identify_attempts: int
    line_debounce_frames: int
    line_hysteresis: float
    batch_size: int
    flush_interval_s: float
    camera_refresh_s: float
    embedding_refresh_s: float
    heartbeat_s: float
    reconnect_max_s: float
    snapshots_enabled: bool
    shard_index: int
    shard_count: int

    @classmethod
    def from_env(cls) -> "Settings":
        shard_count = int(os.environ.get("ENGINE_SHARD_COUNT", "1"))
        shard_index = int(os.environ.get("ENGINE_SHARD_INDEX", "0"))
        if shard_count < 1 or not 0 <= shard_index < shard_count:
            raise ValueError("ENGINE_SHARD_INDEX must be in [0, ENGINE_SHARD_COUNT)")

        settings = cls(
            api_base_url=_required("API_BASE_URL").rstrip("/"),
            service_api_key=_required("SERVICE_API_KEY"),
            yolo_weights=os.environ.get("YOLO_WEIGHTS", "/models/yolo11-face.pt"),
            face_class_id=int(os.environ.get("YOLO_FACE_CLASS_ID", "0")),
            detect_confidence=float(os.environ.get("DETECT_CONFIDENCE", "0.4")),
            detect_imgsz=int(os.environ.get("DETECT_IMGSZ", "1280")),
            person_weights=os.environ.get("PERSON_WEIGHTS", "/models/yolo11n.pt"),
            person_confidence=float(os.environ.get("PERSON_CONFIDENCE", "0.35")),
            person_imgsz=int(os.environ.get("PERSON_IMGSZ", "960")),
            inference_device=os.environ.get("INFERENCE_DEVICE", "cpu"),
            frame_stride=max(1, int(os.environ.get("FRAME_STRIDE", "3"))),
            insightface_model=os.environ.get("INSIGHTFACE_MODEL", "buffalo_l"),
            insightface_det_size=int(os.environ.get("INSIGHTFACE_DET_SIZE", "320")),
            face_match_threshold=float(os.environ.get("FACE_MATCH_THRESHOLD", "0.45")),
            identify_every_n_frames=max(1, int(os.environ.get("IDENTIFY_EVERY_N_FRAMES", "10"))),
            max_identify_attempts=max(1, int(os.environ.get("MAX_IDENTIFY_ATTEMPTS", "3"))),
            line_debounce_frames=max(1, int(os.environ.get("LINE_DEBOUNCE_FRAMES", "3"))),
            line_hysteresis=float(os.environ.get("LINE_HYSTERESIS", "0.02")),
            batch_size=max(1, int(os.environ.get("EVENT_BATCH_SIZE", "50"))),
            flush_interval_s=float(os.environ.get("EVENT_FLUSH_INTERVAL_S", "2")),
            camera_refresh_s=float(os.environ.get("CAMERA_REFRESH_S", "60")),
            embedding_refresh_s=float(os.environ.get("EMBEDDING_REFRESH_S", "300")),
            heartbeat_s=float(os.environ.get("HEARTBEAT_S", "30")),
            reconnect_max_s=float(os.environ.get("RECONNECT_MAX_S", "60")),
            snapshots_enabled=_bool("SNAPSHOTS_ENABLED", True),
            shard_index=shard_index,
            shard_count=shard_count,
        )
        if not 0.0 < settings.face_match_threshold < 1.0:
            raise ValueError("FACE_MATCH_THRESHOLD must be between 0 and 1")
        return settings
