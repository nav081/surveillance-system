"""Batches attendance events and delivers them to the Admin Portal.

Events are grouped per camera (the ingest contract takes one camera per request).
A single background thread drains the queue, so camera workers never block on HTTP.
"""

from __future__ import annotations

import logging
import queue
import threading
import time
from collections import defaultdict
from uuid import UUID

from .api_client import ApiClient, ApiError
from .config import Settings

log = logging.getLogger(__name__)

_STOP = object()


class EventSink:
    def __init__(self, api: ApiClient, settings: Settings) -> None:
        self._api = api
        self._batch_size = settings.batch_size
        self._flush_interval = settings.flush_interval_s
        self._queue: queue.Queue = queue.Queue(maxsize=10_000)
        self._thread = threading.Thread(target=self._run, name="event-sink", daemon=True)

    def start(self) -> None:
        self._thread.start()

    def submit(self, camera_id: UUID, event: dict) -> None:
        try:
            self._queue.put_nowait((camera_id, event))
        except queue.Full:
            log.error("Event queue full; dropping %s event for camera %s", event["eventType"], camera_id)

    def stop(self, timeout: float = 10.0) -> None:
        self._queue.put(_STOP)
        self._thread.join(timeout)

    def _run(self) -> None:
        pending: dict[UUID, list[dict]] = defaultdict(list)
        last_flush = time.monotonic()
        running = True
        while running:
            try:
                item = self._queue.get(timeout=0.25)
                if item is _STOP:
                    running = False
                else:
                    camera_id, event = item
                    pending[camera_id].append(event)
            except queue.Empty:
                pass

            due = time.monotonic() - last_flush >= self._flush_interval
            full = any(len(events) >= self._batch_size for events in pending.values())
            if pending and (due or full or not running):
                self._flush(pending)
                pending.clear()
                last_flush = time.monotonic()

    def _flush(self, pending: dict[UUID, list[dict]]) -> None:
        for camera_id, events in pending.items():
            for start in range(0, len(events), self._batch_size):
                chunk = events[start : start + self._batch_size]
                try:
                    result = self._api.ingest_events(camera_id, chunk)
                    log.info("Camera %s: accepted=%s ignored=%s", camera_id, result.get("accepted"), result.get("ignored"))
                except ApiError as exc:
                    # 4xx means the payload is invalid and retrying will not help. Log and drop.
                    # 5xx and transport errors were already retried inside ApiClient.
                    log.error("Dropping %d events for camera %s: %s", len(chunk), camera_id, exc)
