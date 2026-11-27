from engine.api_client import _normalize_scheme


def test_normalize_scheme_lowercases_scheme_and_keeps_credentials():
    assert _normalize_scheme("RTSP://user:Pw%40x@192.168.1.5/stream1") == "rtsp://user:Pw%40x@192.168.1.5/stream1"


def test_normalize_scheme_leaves_lowercase_urls_unchanged():
    url = "rtsp://user:pass@10.0.0.2:554/live"
    assert _normalize_scheme(url) == url
