import numpy as np

from engine.geometry import expand_box, largest_box


def test_expand_box_pads_and_clamps_to_frame():
    assert expand_box([100, 100, 200, 300], 0.5, 1920, 1080) == (50, 0, 250, 400)
    assert expand_box([0, 0, 100, 100], 0.5, 1920, 1080) == (0, 0, 150, 150)
    assert expand_box([1800, 1000, 1900, 1070], 0.5, 1920, 1080) == (1750, 965, 1920, 1080)


def test_expand_box_zero_margin_keeps_box():
    assert expand_box([10.7, 20.2, 50.0, 80.0], 0.0, 100, 100) == (10, 20, 50, 80)


def test_largest_box_picks_biggest_area():
    dets = np.array(
        [
            [0, 0, 10, 10, 0.9],
            [5, 5, 60, 40, 0.5],
            [100, 100, 120, 130, 0.8],
        ],
        dtype=np.float32,
    )
    np.testing.assert_array_equal(largest_box(dets), dets[1])


def test_largest_box_empty_returns_none():
    assert largest_box(np.zeros((0, 5), dtype=np.float32)) is None
    assert largest_box(None) is None
