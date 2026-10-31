import io
import unittest
import numpy as np
from PIL import Image
from pipeline import background_mask, png_bytes, read_image, read_subject_mask, read_subject_selection, refine_selected


class LayerContractTests(unittest.TestCase):
    def test_selection_runs_matting_only_on_roi_excludes_painted_background_and_keeps_native_alpha(self):
        prior = np.zeros((300, 400), dtype=np.uint8); prior[50:150, 60:140] = 255; prior[49, 60:140] = 128
        add = np.zeros_like(prior); add[180:220, 220:260] = 255
        erase = np.zeros_like(prior); erase[70:90, 70:90] = 255
        selection = Image.merge("RGB", tuple(Image.fromarray(x) for x in (prior, add, erase)))
        decoded = read_subject_selection(png_bytes(selection), 400, 300)
        calls = []
        def matte(roi):
            calls.append(roi.size)
            alpha = Image.new("L", roi.size, 0); alpha.paste(128, (0, 0, roi.width // 2, roi.height)); return alpha
        result = np.asarray(refine_selected(Image.new("RGB", (400, 300)), decoded, matte))
        self.assertEqual(calls, [(104, 104)])
        self.assertTrue(np.all(result[(add == 0) & (erase == 0)] == prior[(add == 0) & (erase == 0)]))
        self.assertEqual(result[200, 225], 128)  # Soft BiRefNet alpha, not solid brush paint.
        self.assertEqual(result[200, 255], 0)  # Painted background stays background.
        self.assertEqual(result[80, 80], 0)
        self.assertEqual(result[49, 70], 128)

    def test_eraser_only_preserves_other_pixels_without_running_matting(self):
        prior = np.zeros((64, 64), dtype=np.uint8); prior[20:40, 20:40] = 128
        add = np.zeros_like(prior); erase = np.zeros_like(prior); erase[25:30, 25:30] = 255
        selection = Image.merge("RGB", tuple(Image.fromarray(x) for x in (prior, add, erase)))
        result = np.asarray(refine_selected(Image.new("RGB", (64, 64)), selection,
            lambda _: self.fail("Erasers must never trigger positive matting.")))
        self.assertTrue(np.all(result[erase == 0] == prior[erase == 0])); self.assertEqual(result[26, 26], 0)

    def test_empty_or_transparent_selection_is_rejected(self):
        prior = Image.new("L", (64, 64), 0); prior.paste(128, (20, 20, 40, 40))
        empty = Image.new("L", (64, 64), 0)
        for selection in (Image.merge("RGB", (prior, empty, empty)), Image.new("RGBA", (64, 64), (255, 255, 255, 0))):
            with self.assertRaises(ValueError): read_subject_selection(png_bytes(selection), 64, 64)

    def test_png_keeps_soft_foreground_alpha_and_exact_canvas(self):
        foreground = Image.new("RGBA", (20, 30), (20, 40, 60, 127))
        with Image.open(io.BytesIO(png_bytes(foreground))) as decoded:
            self.assertEqual(decoded.size, (20, 30))
            self.assertEqual(decoded.getpixel((10, 10)), (20, 40, 60, 127))

    def test_inpainting_mask_includes_soft_edges_and_expands_footprint(self):
        values = np.zeros((64, 64), dtype=np.uint8)
        values[20:40, 20:40] = 255
        values[19, 20:40] = 5
        holes = background_mask(Image.fromarray(values))
        self.assertEqual(holes[18, 20], 1)
        self.assertEqual(holes[0, 0], 0)
        self.assertEqual(values[18, 20], 0)

    def test_no_foreground_or_no_background_is_rejected(self):
        for value in (0, 255):
            with self.assertRaises(ValueError):
                background_mask(Image.new("L", (64, 64), value))

    def test_mismatched_dimensions_are_rejected(self):
        with self.assertRaises(ValueError):
            read_image(png_bytes(Image.new("RGB", (20, 30))), 30, 20)

    def test_manual_mask_preserves_soft_edges_and_restored_or_erased_areas(self):
        mask = Image.new("L", (64, 64), 0)
        mask.paste(255, (20, 20, 40, 40))
        mask.putpixel((19, 25), 128)
        mask.putpixel((30, 30), 0)
        decoded = read_subject_mask(png_bytes(mask), 64, 64)
        self.assertEqual(decoded.tobytes(), mask.tobytes())
        self.assertEqual(background_mask(decoded)[0, 0], 0)

    def test_invalid_manual_masks_are_rejected(self):
        for mask in [Image.new("L", (64, 64), 0), Image.new("L", (64, 64), 255),
                     Image.new("RGBA", (64, 64), (255, 255, 255, 0)), Image.new("L", (32, 32), 127)]:
            with self.assertRaises(ValueError):
                read_subject_mask(png_bytes(mask), 64, 64)


if __name__ == "__main__":
    unittest.main()
