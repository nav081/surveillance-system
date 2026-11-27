"""Entry/exit detection by counting tracked centroids crossing a virtual line.

Pure logic with no third-party dependencies, so it is unit-testable in isolation.
A crossing is only emitted when the new side is stable for `debounce_frames`
consecutive observations and the centroid is beyond `hysteresis` from the line.
This suppresses jitter from detector noise around the line.
"""

from __future__ import annotations

from dataclasses import dataclass

ENTRY = "entry"
EXIT = "exit"

TOP_TO_BOTTOM = "TopToBottom"
BOTTOM_TO_TOP = "BottomToTop"

_BEFORE = -1  # above (or left of) the line
_AFTER = 1  # below (or right of) the line


@dataclass
class _TrackState:
    committed: int | None = None
    pending: int | None = None
    pending_count: int = 0


class LineCounter:
    def __init__(
        self,
        direction: str,
        line_position: float,
        debounce_frames: int = 3,
        hysteresis: float = 0.02,
    ) -> None:
        if direction not in (TOP_TO_BOTTOM, BOTTOM_TO_TOP):
            raise ValueError(f"Unsupported entry direction: {direction}")
        if not 0.0 < line_position < 1.0:
            raise ValueError("line_position must be between 0 and 1")
        self._direction = direction
        self._line = line_position
        self._debounce = max(1, debounce_frames)
        self._hysteresis = hysteresis
        self._tracks: dict[int, _TrackState] = {}

    def update(self, track_id: int, position: float) -> str | None:
        """Feed a normalized (0..1) centroid position along the axis perpendicular to the line.

        Returns "entry", "exit", or None.
        """
        state = self._tracks.setdefault(track_id, _TrackState())
        side = self._side(position)
        if side is None:
            # Inside the hysteresis band: neither commit nor reset progress.
            return None

        if side == state.committed:
            state.pending = None
            state.pending_count = 0
            return None

        if state.committed is None:
            # First confident observation establishes a baseline without emitting an event.
            state.committed = side
            return None

        if side == state.pending:
            state.pending_count += 1
        else:
            state.pending = side
            state.pending_count = 1

        if state.pending_count < self._debounce:
            return None

        previous = state.committed
        state.committed = side
        state.pending = None
        state.pending_count = 0
        return self._classify(previous, side)

    def prune(self, active_track_ids: set[int]) -> None:
        for track_id in list(self._tracks):
            if track_id not in active_track_ids:
                del self._tracks[track_id]

    def _side(self, position: float) -> int | None:
        if position < self._line - self._hysteresis:
            return _BEFORE
        if position > self._line + self._hysteresis:
            return _AFTER
        return None

    def _classify(self, previous: int, current: int) -> str:
        moving_towards_after = previous == _BEFORE and current == _AFTER
        if self._direction == TOP_TO_BOTTOM:
            return ENTRY if moving_towards_after else EXIT
        return EXIT if moving_towards_after else ENTRY
