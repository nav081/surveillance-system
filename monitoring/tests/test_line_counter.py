import pytest

from engine.line_counter import BOTTOM_TO_TOP, ENTRY, EXIT, TOP_TO_BOTTOM, LineCounter


def _feed(counter: LineCounter, track_id: int, positions: list[float]) -> list[str]:
    return [e for p in positions if (e := counter.update(track_id, p)) is not None]


def test_top_to_bottom_crossing_is_entry():
    counter = LineCounter(TOP_TO_BOTTOM, 0.5, debounce_frames=2, hysteresis=0.02)
    events = _feed(counter, 1, [0.2, 0.2, 0.8, 0.8, 0.8])
    assert events == [ENTRY]


def test_top_to_bottom_reverse_crossing_is_exit():
    counter = LineCounter(TOP_TO_BOTTOM, 0.5, debounce_frames=2)
    events = _feed(counter, 1, [0.8, 0.8, 0.2, 0.2])
    assert events == [EXIT]


def test_bottom_to_top_inverts_direction():
    counter = LineCounter(BOTTOM_TO_TOP, 0.5, debounce_frames=2)
    assert _feed(counter, 1, [0.8, 0.8, 0.2, 0.2]) == [ENTRY]


def test_first_observation_is_baseline_not_event():
    counter = LineCounter(TOP_TO_BOTTOM, 0.5, debounce_frames=1)
    assert _feed(counter, 1, [0.9, 0.9, 0.9]) == []


def test_jitter_around_line_does_not_emit():
    counter = LineCounter(TOP_TO_BOTTOM, 0.5, debounce_frames=1, hysteresis=0.05)
    # Oscillation inside the hysteresis band (0.45..0.55) never commits a side.
    assert _feed(counter, 1, [0.2, 0.52, 0.48, 0.53, 0.47, 0.51]) == []


def test_single_frame_blip_is_rejected_by_debounce():
    counter = LineCounter(TOP_TO_BOTTOM, 0.5, debounce_frames=3)
    # Committed top; one frame below then back to top must not emit.
    assert _feed(counter, 1, [0.2, 0.2, 0.8, 0.2, 0.2, 0.2]) == []


def test_each_track_is_independent():
    counter = LineCounter(TOP_TO_BOTTOM, 0.5, debounce_frames=1)
    assert _feed(counter, 1, [0.2, 0.8]) == [ENTRY]
    assert _feed(counter, 2, [0.2, 0.8]) == [ENTRY]


def test_prune_forgets_lost_tracks():
    counter = LineCounter(TOP_TO_BOTTOM, 0.5, debounce_frames=1)
    counter.update(1, 0.2)
    counter.prune(active_track_ids=set())
    # After pruning, the track is treated as new and its first observation is a baseline again.
    assert counter.update(1, 0.8) is None


@pytest.mark.parametrize("line", [0.0, 1.0, -0.1])
def test_invalid_line_position_is_rejected(line):
    with pytest.raises(ValueError):
        LineCounter(TOP_TO_BOTTOM, line)


def test_unknown_direction_is_rejected():
    with pytest.raises(ValueError):
        LineCounter("LeftToRight", 0.5)
