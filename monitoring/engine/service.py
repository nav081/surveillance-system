"""Engine entry point: reconciles per-camera workers against the Admin Portal's active camera list."""

from __future__ import annotations

import logging
import signal
import threading
import time
import zlib
from uuid import UUID

from .api_client import ActiveCamera, ApiClient, ApiError
from .config import Settings
from .detector import FaceDetector, FaceRecognizer, PersonDetector
from .event_sink import EventSink
from .camera_worker import CameraWorker
from .matcher import EmbeddingIndex

log = logging.getLogger("engine")


def _owned_by_shard(camera_id: UUID, settings: Settings) -> bool:
    # crc32 is stable across processes, unlike Python's salted hash().
    return zlib.crc32(str(camera_id).encode()) % settings.shard_count == settings.shard_index


def _reconcile(
    cameras: list[ActiveCamera],
    workers: dict[UUID, CameraWorker],
    settings: Settings,
    start_worker,
) -> None:
    wanted = {c.camera_id: c for c in cameras if _owned_by_shard(c.camera_id, settings)}

    for camera_id, worker in list(workers.items()):
        desired = wanted.get(camera_id)
        if desired is None or desired.signature() != worker.camera.signature() or not worker.is_alive():
            worker.stop()
            del workers[camera_id]
            log.info("Stopped worker for camera %s", camera_id)

    for camera_id, camera in wanted.items():
        if camera_id not in workers:
            workers[camera_id] = start_worker(camera)
            log.info("Started worker for camera %s (%s)", camera.name, camera_id)

    log.info("Shard %d/%d running %d cameras", settings.shard_index, settings.shard_count, len(workers))


def run() -> None:
    settings = Settings.from_env()
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s %(levelname)s %(name)s: %(message)s",
    )

    api = ApiClient(settings)
    detector = FaceDetector(settings)
    person_detector = PersonDetector(settings)
    recognizer = FaceRecognizer(settings)
    index = EmbeddingIndex(settings.face_match_threshold)
    index.load(api.load_embeddings())
    log.info("Loaded face embeddings for matching")

    sink = EventSink(api, settings)
    sink.start()

    def start_worker(camera: ActiveCamera) -> CameraWorker:
        worker = CameraWorker(camera, settings, person_detector, detector, recognizer, index, api, sink)
        worker.start()
        return worker

    stop = threading.Event()
    for sig in (signal.SIGINT, signal.SIGTERM):
        signal.signal(sig, lambda *_: stop.set())

    workers: dict[UUID, CameraWorker] = {}
    next_camera_refresh = 0.0
    next_embedding_refresh = time.monotonic() + settings.embedding_refresh_s

    while not stop.is_set():
        now = time.monotonic()
        if now >= next_embedding_refresh:
            next_embedding_refresh = now + settings.embedding_refresh_s
            try:
                index.load(api.load_embeddings())
            except ApiError as exc:
                log.warning("Embedding refresh failed; keeping previous index: %s", exc)

        if now >= next_camera_refresh:
            next_camera_refresh = now + settings.camera_refresh_s
            try:
                _reconcile(api.list_active_cameras(), workers, settings, start_worker)
            except ApiError as exc:
                log.warning("Camera refresh failed; keeping current workers: %s", exc)

        stop.wait(1.0)

    log.info("Shutting down %d camera workers", len(workers))
    for worker in workers.values():
        worker.stop()
    sink.stop()
    api.close()
