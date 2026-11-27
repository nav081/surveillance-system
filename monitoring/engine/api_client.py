from __future__ import annotations

import logging
import time
from dataclasses import dataclass
from uuid import UUID

import httpx

from .config import Settings

log = logging.getLogger(__name__)


@dataclass(frozen=True)
class ActiveCamera:
    camera_id: UUID
    tenant_id: UUID
    home_id: UUID
    name: str
    rtsp_url: str
    entry_direction: str
    line_position: float

    def signature(self) -> tuple[str, str, float, str]:
        return (self.rtsp_url, self.entry_direction, self.line_position, self.name)


class ApiError(RuntimeError):
    def __init__(self, status: int, message: str) -> None:
        super().__init__(f"API returned {status}: {message}")
        self.status = status


def _normalize_scheme(url: str) -> str:
    """FFmpeg only recognizes lowercase schemes, so "RTSP://" must become "rtsp://"."""
    scheme, sep, rest = url.partition("://")
    return f"{scheme.lower()}{sep}{rest}" if sep else url


class ApiClient:
    """Thin client for the Admin Portal's /api/internal endpoints."""

    def __init__(self, settings: Settings, transport: httpx.BaseTransport | None = None) -> None:
        self._client = httpx.Client(
            base_url=settings.api_base_url,
            headers={"X-Service-Key": settings.service_api_key},
            timeout=httpx.Timeout(15.0, connect=5.0),
            transport=transport,
        )

    def close(self) -> None:
        self._client.close()

    def _request(self, method: str, path: str, *, retries: int = 3, **kwargs) -> httpx.Response:
        delay = 1.0
        for attempt in range(1, retries + 1):
            try:
                response = self._client.request(method, path, **kwargs)
            except httpx.TransportError as exc:
                if attempt == retries:
                    raise ApiError(0, f"transport failure: {exc.__class__.__name__}") from exc
            else:
                if response.status_code < 500 or attempt == retries:
                    if response.status_code >= 400:
                        raise ApiError(response.status_code, response.text[:300])
                    return response
            time.sleep(delay)
            delay = min(delay * 2, 30.0)
        raise ApiError(0, "unreachable")

    def list_active_cameras(self) -> list[ActiveCamera]:
        payload = self._request("GET", "/api/internal/cameras/active").json()
        return [
            ActiveCamera(
                camera_id=UUID(item["cameraId"]),
                tenant_id=UUID(item["tenantId"]),
                home_id=UUID(item["homeId"]),
                name=item["name"],
                rtsp_url=_normalize_scheme(item["rtspUrl"]),
                entry_direction=item["entryDirection"],
                line_position=float(item["linePosition"]),
            )
            for item in payload
        ]

    def heartbeat(self, camera_id: UUID) -> None:
        self._request("POST", f"/api/internal/cameras/{camera_id}/heartbeat", retries=1)

    def load_embeddings(self) -> list[dict]:
        return self._request("GET", "/api/internal/persons/embeddings").json()

    def upload_snapshot(self, camera_id: UUID, jpeg: bytes) -> str:
        response = self._request(
            "POST",
            "/api/internal/snapshots",
            data={"cameraId": str(camera_id)},
            files={"file": ("snapshot.jpg", jpeg, "image/jpeg")},
            retries=2,
        )
        return response.json()["snapshotKey"]

    def ingest_events(self, camera_id: UUID, events: list[dict]) -> dict:
        body = {"cameraId": str(camera_id), "events": events}
        response = self._request("POST", "/api/internal/attendance/events", json=body)
        return response.json()
