from uuid import uuid4

import numpy as np

from engine.matcher import EmbeddingIndex


def _person(tenant, name, vec):
    return {
        "personId": str(uuid4()),
        "tenantId": str(tenant),
        "displayName": name,
        "embedding": list(map(float, vec)),
    }


def test_matches_closest_person_above_threshold():
    tenant = uuid4()
    index = EmbeddingIndex(threshold=0.5)
    index.load([
        _person(tenant, "Alice", [1, 0, 0]),
        _person(tenant, "Bob", [0, 1, 0]),
    ])
    match = index.match(tenant, np.array([0.9, 0.1, 0.0]))
    assert match is not None and match.display_name == "Alice"
    assert match.score > 0.9


def test_rejects_below_threshold():
    tenant = uuid4()
    index = EmbeddingIndex(threshold=0.5)
    index.load([_person(tenant, "Alice", [1, 0, 0])])
    assert index.match(tenant, np.array([0.0, 1.0, 0.0])) is None


def test_tenants_are_isolated():
    tenant_a, tenant_b = uuid4(), uuid4()
    index = EmbeddingIndex(threshold=0.5)
    index.load([_person(tenant_a, "Alice", [1, 0, 0])])
    assert index.match(tenant_b, np.array([1.0, 0.0, 0.0])) is None


def test_zero_vector_query_returns_none():
    tenant = uuid4()
    index = EmbeddingIndex(threshold=0.5)
    index.load([_person(tenant, "Alice", [1, 0, 0])])
    assert index.match(tenant, np.zeros(3)) is None


def test_unloaded_index_returns_none():
    assert EmbeddingIndex(threshold=0.5).match(uuid4(), np.array([1.0, 0.0])) is None
