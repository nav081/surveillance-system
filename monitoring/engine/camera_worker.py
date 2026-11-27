"""One thread per camera: RTSP read, face detection, tracking, identity, and entry/exit events."""

from __future__ import annotations

import logging
import threading
import time
from dataclasses import dataclass
from datetime import datetime, timezone
from uuid import UUID

import cv2
import numpy as np
import supervision as sv

from .api_client import ActiveCamera, ApiClient, ApiError
from .config import Settings
from .detector import FaceDetector, FaceRecognizer, PersonDetector
from .event_sink import EventSink
from .geometry import expand_box, largest_box
from .line_counter import ENTRY, LineCounter
from .matcher import EmbeddingIndex

log = logging.getLogger(__name__)

_CROP_MARGIN = 0.35
_FACE_CROP_MARGIN = 0.5
_PRESENCE_TTL_S = 900.0


@dataclass
class _Identity:
    person_id: UUID | None = None
    display_name: str | None = None
    attempts: int = 0


@dataclass
class _Presence:
    key: str
    person_id: UUID | None
    display_name: str | None
    last_seen: float


def _crop(frame: np.ndarray, xyxy: np.ndarray, margin: float) -> np.ndarray:
    left, top, right, bottom = expand_box(xyxy, margin, frame.shape[1], frame.shape[0])
    return frame[top:bottom, left:right]


class CameraWorker(threading.Thread):
    def __init__(
        self,
        camera: ActiveCamera,
        settings: Settings,
        person_detector: PersonDetector,
        detector: FaceDetector,
        recognizer: FaceRecognizer,
        index: EmbeddingIndex,
        api: ApiClient,
        sink: EventSink,
    ) -> None:
        super().__init__(name=f"camera-{camera.camera_id}", daemon=True)
        self.camera = camera
        self._settings = settings
        self._person_detector = person_detector
        self._detector = detector
        self._recognizer = recognizer
        self._index = index
        self._api = api
        self._sink = sink
        self._stop_event = threading.Event()
        self._identities: dict[int, _Identity] = {}
        self._presence: dict[int, _Presence] = {}

    def stop(self) -> None:
        self._stop_event.set()

    def run(self) -> None:
        # The RTSP URL carries credentials, so logs reference cameras by name and id only.
        backoff = 2.0
        while not self._stop_event.is_set():
            cap = cv2.VideoCapture(self.camera.rtsp_url, cv2.CAP_FFMPEG)
            cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)
            if not cap.isOpened():
                cap.release()
                log.warning("Camera %s failed to open; retrying in %.0fs", self.camera.name, backoff)
                self._sleep(backoff)
                backoff = min(backoff * 2, self._settings.reconnect_max_s)
                continue

            log.info("Camera %s connected", self.camera.name)
            backoff = 2.0
            try:
                self._read_loop(cap)
            except Exception:
                log.exception("Camera %s worker error; reconnecting", self.camera.name)
            finally:
                cap.release()
            self._sleep(backoff)

    def _sleep(self, seconds: float) -> None:
        self._stop_event.wait(seconds)

    def _read_loop(self, cap: cv2.VideoCapture) -> None:
        fps = cap.get(cv2.CAP_PROP_FPS) or 15.0
        stride = self._settings.frame_stride
        tracker = sv.ByteTrack(frame_rate=max(1, int(fps / stride)))
        counter = LineCounter(
            direction=self.camera.entry_direction,
            line_position=self.camera.line_position,
            debounce_frames=self._settings.line_debounce_frames,
            hysteresis=self._settings.line_hysteresis,
        )
        processed = 0
        last_heartbeat = 0.0

        while not self._stop_event.is_set():
            ok, frame = cap.read()
            if not ok or frame is None:
                log.warning("Camera %s: frame read failed", self.camera.name)
                return

            now = time.monotonic()
            if now - last_heartbeat >= self._settings.heartbeat_s:
                last_heartbeat = now
                self._heartbeat()
            self._prune_presence(now)

            if (processed := processed + 1) % stride:
                continue
            self._process(frame, processed, tracker, counter)

    def _heartbeat(self) -> None:
        try:
            self._api.heartbeat(self.camera.camera_id)
        except ApiError as exc:
            log.warning("Heartbeat failed for camera %s: %s", self.camera.name, exc)

    def _process(self, frame: np.ndarray, processed: int, tracker: sv.ByteTrack, counter: LineCounter) -> None:
        h = frame.shape[0]
        detections = self._person_detector.detect(frame)
        if len(detections):
            sv_dets = sv.Detections(
                xyxy=detections[:, :4],
                confidence=detections[:, 4],
                class_id=np.zeros(len(detections), dtype=int),
            )
        else:
            sv_dets = sv.Detections.empty()

        tracked = tracker.update_with_detections(sv_dets)
        if tracked.tracker_id is None:
            return

        active: set[int] = set()
        for xyxy, raw_id in zip(tracked.xyxy, tracked.tracker_id):
            track_id = int(raw_id)
            active.add(track_id)
            identity = self._identities.setdefault(track_id, _Identity())

            if track_id in self._presence:
                self._presence[track_id].last_seen = time.monotonic()

            if (
                identity.person_id is None
                and identity.attempts < self._settings.max_identify_attempts
                and processed % self._settings.identify_every_n_frames == 0
            ):
                self._try_identify(frame, xyxy, identity)

            center_y = ((xyxy[1] + xyxy[3]) / 2.0) / h
            event = counter.update(track_id, center_y)
            if event is not None:
                self._record(event, frame, xyxy, track_id, identity)

        counter.prune(active)
        for track_id in list(self._identities):
            if track_id not in active:
                del self._identities[track_id]

    def _try_identify(self, frame: np.ndarray, person_xyxy: np.ndarray, identity: _Identity) -> None:
        # Faces are searched only inside the tracked person's box. Attempts are counted only when
        # a face was actually found, so people who turn away are retried on later frames.
        h, w = frame.shape[:2]
        left, top, right, bottom = expand_box(person_xyxy, 0.0, w, h)
        person_crop = frame[top:bottom, left:right]
        if person_crop.size == 0:
            return
        face_boxes = self._detector.detect(person_crop)
        face = largest_box(face_boxes)
        if face is None:
            return

        identity.attempts += 1
        face_xyxy = face[:4] + np.array([left, top, left, top], dtype=np.float32)
        embedding = self._recognizer.embed(_crop(frame, face_xyxy, _FACE_CROP_MARGIN))
        if embedding is None:
            return
        match = self._index.match(self.camera.tenant_id, embedding)
        if match is not None:
            identity.person_id = match.person_id
            identity.display_name = match.display_name

    def _record(self, event: str, frame: np.ndarray, xyxy: np.ndarray, track_id: int, identity: _Identity) -> None:
        # An exit reuses the key from the matching entry, so a track that was only recognized
        # after entering still closes the same presence record.
        if event == ENTRY:
            presence = self._new_presence(track_id, identity)
            self._presence[track_id] = presence
        elif track_id in self._presence:
            presence = self._presence.pop(track_id)
        else:
            # Exit without a known entry (for example, someone already inside when the engine
            # started). The server ignores it because no open presence exists.
            presence = self._new_presence(track_id, identity)

        snapshot_key = self._upload_snapshot(frame, xyxy) if self._settings.snapshots_enabled else None
        payload = {
            "eventType": event,
            "personKey": presence.key,
            "personId": str(presence.person_id) if presence.person_id else None,
            "displayName": presence.display_name,
            "occurredAt": datetime.now(timezone.utc).isoformat(),
            "confidence": None,
            "snapshotKey": snapshot_key,
        }
        log.info("Camera %s: %s for %s", self.camera.name, event, presence.key)
        self._sink.submit(self.camera.camera_id, payload)

    def _new_presence(self, track_id: int, identity: _Identity) -> _Presence:
        if identity.person_id is not None:
            key = f"person:{identity.person_id}"
        else:
            key = f"track:{self.camera.camera_id}:{track_id}"
        return _Presence(key, identity.person_id, identity.display_name, time.monotonic())

    def _upload_snapshot(self, frame: np.ndarray, xyxy: np.ndarray) -> str | None:
        crop = _crop(frame, xyxy, _CROP_MARGIN)
        if crop.size == 0:
            return None
        ok, buffer = cv2.imencode(".jpg", crop, [cv2.IMWRITE_JPEG_QUALITY, 85])
        if not ok:
            return None
        try:
            return self._api.upload_snapshot(self.camera.camera_id, buffer.tobytes())
        except ApiError as exc:
            log.warning("Snapshot upload failed for camera %s: %s", self.camera.name, exc)
            return None

    def _prune_presence(self, now: float) -> None:
        stale = [t for t, p in self._presence.items() if now - p.last_seen > _PRESENCE_TTL_S]
        for track_id in stale:
            log.warning("Dropping stale presence for track %s on camera %s", track_id, self.camera.name)
            del self._presence[track_id]
