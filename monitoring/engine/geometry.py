"""Pure box helpers shared by the camera worker. Kept free of OpenCV and model imports."""

from __future__ import annotations

import numpy as np


def expand_box(xyxy, margin: float, width: int, height: int) -> tuple[int, int, int, int]:
    """Pads a box by `margin` of its size on each side and clamps it to the frame."""
    x1, y1, x2, y2 = (float(v) for v in xyxy[:4])
    pad_x = (x2 - x1) * margin
    pad_y = (y2 - y1) * margin
    left = int(max(0, x1 - pad_x))
    top = int(max(0, y1 - pad_y))
    right = int(min(width, x2 + pad_x))
    bottom = int(min(height, y2 + pad_y))
    return left, top, right, bottom


def largest_box(detections: np.ndarray) -> np.ndarray | None:
    """Returns the row with the largest area from (n, >=4) x1, y1, x2, y2 rows, or None if empty."""
    if detections is None or len(detections) == 0:
        return None
    areas = (detections[:, 2] - detections[:, 0]) * (detections[:, 3] - detections[:, 1])
    return detections[int(np.argmax(areas))]
