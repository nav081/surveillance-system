"""Cosine-similarity matching of face embeddings against enrolled persons, partitioned by tenant."""

from __future__ import annotations

from dataclasses import dataclass
from uuid import UUID

import numpy as np


@dataclass(frozen=True)
class Match:
    person_id: UUID
    display_name: str
    score: float


@dataclass
class _TenantIndex:
    person_ids: list[UUID]
    names: list[str]
    matrix: np.ndarray  # shape (n, d), rows L2-normalized


class EmbeddingIndex:
    def __init__(self, threshold: float) -> None:
        self._threshold = threshold
        self._tenants: dict[UUID, _TenantIndex] = {}

    def load(self, persons: list[dict]) -> None:
        """Replace the whole index. Each item: {personId, tenantId, displayName, embedding}."""
        grouped: dict[UUID, list[dict]] = {}
        for person in persons:
            grouped.setdefault(UUID(person["tenantId"]), []).append(person)

        tenants: dict[UUID, _TenantIndex] = {}
        for tenant_id, items in grouped.items():
            vectors = np.array([item["embedding"] for item in items], dtype=np.float32)
            norms = np.linalg.norm(vectors, axis=1, keepdims=True)
            norms[norms < 1e-6] = 1.0
            tenants[tenant_id] = _TenantIndex(
                person_ids=[UUID(item["personId"]) for item in items],
                names=[item["displayName"] for item in items],
                matrix=vectors / norms,
            )
        self._tenants = tenants

    def match(self, tenant_id: UUID, embedding: np.ndarray) -> Match | None:
        index = self._tenants.get(tenant_id)
        if index is None or index.matrix.size == 0:
            return None

        query = np.asarray(embedding, dtype=np.float32).reshape(-1)
        norm = np.linalg.norm(query)
        if norm < 1e-6:
            return None
        scores = index.matrix @ (query / norm)
        best = int(np.argmax(scores))
        best_score = float(scores[best])
        if best_score < self._threshold:
            return None
        return Match(index.person_ids[best], index.names[best], best_score)
