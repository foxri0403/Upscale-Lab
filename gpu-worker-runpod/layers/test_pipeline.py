import io
import unittest
import numpy as np
from PIL import Image
from pipeline import background_mask, png_bytes, read_image


class LayerContractTests(unittest.TestCase):
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


if __name__ == "__main__":
    unittest.main()
