from __future__ import annotations

import sys
import unittest
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'backend' / 'ai-worker'))
from generic_quality_refiner import refine_layer_generic


class TestGenericQualityRefiner(unittest.TestCase):
    def rgba(self, width=40, height=40):
        return np.zeros((height, width, 4), dtype=np.uint8)

    def test_empty_unchanged(self):
        arr = self.rgba()
        result, stats = refine_layer_generic(Image.fromarray(arr, 'RGBA'), 'face')
        np.testing.assert_array_equal(np.array(result), arr)
        self.assertEqual(stats['reason'], 'empty-layer')

    def test_preserve_opaque_white_eye_in_dark_shape(self):
        arr = self.rgba()
        arr[5:35, 5:35] = [20, 20, 20, 255]
        arr[15:25, 15:25] = [255, 255, 255, 255]
        out, stats = refine_layer_generic(Image.fromarray(arr, 'RGBA'), 'eyewhite', 'Face')
        np.testing.assert_array_equal(np.asarray(out), arr)
        self.assertEqual(stats['changedAlphaPixels'], 0)

    def test_preserve_single_opaque_facial_stroke(self):
        arr = self.rgba()
        arr[8, 8] = [50, 40, 30, 255]
        result, _ = refine_layer_generic(Image.fromarray(arr, 'RGBA'), 'eyebrow', 'Face')
        self.assertEqual(int(np.array(result)[8, 8, 3]), 255)

    def test_remove_faint_isolated_speckle_when_safe(self):
        arr = self.rgba()
        arr[5:35, 5:35] = [90, 90, 90, 255]
        arr[1, 1] = [110, 80, 75, 7]
        result, stats = refine_layer_generic(Image.fromarray(arr, 'RGBA'), 'back hair', 'Hair')
        self.assertEqual(int(np.array(result)[1, 1, 3]), 0)
        self.assertEqual(stats['removedWeakSpeckles'], 1)

    def test_repair_similar_color_pinhole(self):
        arr = self.rgba()
        arr[5:35, 5:35] = [60, 70, 80, 255]
        arr[20, 20] = [0, 0, 0, 0]
        result, stats = refine_layer_generic(Image.fromarray(arr, 'RGBA'), 'topwear', 'Clothes')
        self.assertEqual(int(np.array(result)[20, 20, 3]), 246)
        self.assertEqual(stats['repairedOpaquePinholes'], 1)
        np.testing.assert_array_equal(np.array(result)[20, 20, :3], [60, 70, 80])

    def test_different_color_pinhole_not_repaired(self):
        arr = self.rgba()
        arr[5:35, 5:35] = [60, 70, 80, 255]
        arr[19, 20] = [220, 240, 100, 255]
        arr[20, 20] = [0, 0, 0, 0]
        result, stats = refine_layer_generic(Image.fromarray(arr, 'RGBA'), 'topwear', 'Clothes')
        self.assertEqual(int(np.array(result)[20, 20, 3]), 0)
        self.assertEqual(stats['repairedOpaquePinholes'], 0)

    def test_rollback_on_mass_faint_speckles(self):
        arr = self.rgba()
        arr[2:12, 2:12] = [90, 90, 90, 255]
        for y in range(15, 35, 3):
            for x in range(2, 38, 3):
                arr[y, x] = [200, 200, 200, 4]
        result, stats = refine_layer_generic(Image.fromarray(arr, 'RGBA'), 'cloth', 'Clothes')
        self.assertTrue(stats['reverted'])
        np.testing.assert_array_equal(np.array(result), arr)


if __name__ == '__main__':
    unittest.main()
