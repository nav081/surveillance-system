import pytest

from engine.config import Settings

_BASE = {
    "API_BASE_URL": "http://api:8080/",
    "SERVICE_API_KEY": "k",
}


def _set_env(monkeypatch, extra=None):
    for name in list(_BASE) + ["ENGINE_SHARD_COUNT", "ENGINE_SHARD_INDEX"]:
        monkeypatch.delenv(name, raising=False)
    for name, value in {**_BASE, **(extra or {})}.items():
        monkeypatch.setenv(name, value)


def test_loads_defaults_and_strips_trailing_slash(monkeypatch):
    _set_env(monkeypatch)
    settings = Settings.from_env()
    assert settings.api_base_url == "http://api:8080"
    assert settings.shard_count == 1 and settings.shard_index == 0
    assert settings.snapshots_enabled is True


def test_missing_api_key_fails_fast(monkeypatch):
    _set_env(monkeypatch)
    monkeypatch.delenv("SERVICE_API_KEY")
    with pytest.raises(ValueError, match="SERVICE_API_KEY"):
        Settings.from_env()


def test_shard_index_must_be_in_range(monkeypatch):
    _set_env(monkeypatch, {"ENGINE_SHARD_COUNT": "2", "ENGINE_SHARD_INDEX": "2"})
    with pytest.raises(ValueError, match="ENGINE_SHARD_INDEX"):
        Settings.from_env()


def test_threshold_out_of_range_is_rejected(monkeypatch):
    _set_env(monkeypatch, {"FACE_MATCH_THRESHOLD": "1.5"})
    with pytest.raises(ValueError, match="FACE_MATCH_THRESHOLD"):
        Settings.from_env()
