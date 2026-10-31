import hashlib
import json
import sys
import types
import unittest
from unittest.mock import patch

from PIL import Image
from pipeline import png_bytes

# Contract tests run without RunPod, model downloads or a local GPU fallback.
with patch.dict(sys.modules, {"runpod": types.SimpleNamespace(), "requests": types.SimpleNamespace(get=None, put=None)}):
    import handler


class Response:
    status_code = 200

    def __init__(self, content=b""):
        self.content = content

    def __enter__(self):
        return self

    def __exit__(self, *_):
        return False

    def iter_content(self, _):
        yield self.content


class Pipeline:
    def __init__(self):
        self.calls = []

    def split(self, image, alpha=None, selection=None):
        if selection is not None:
            self.selection = selection
            alpha = selection.getchannel("R")
        self.calls.append(alpha)
        foreground = image.convert("RGBA")
        if alpha is not None:
            foreground.putalpha(alpha)
        return image, foreground


class HandlerContractTests(unittest.TestCase):
    def test_selection_reaches_roi_pipeline_and_completion_acknowledges_mode(self):
        prior = Image.new("L", (64, 64), 0); prior.paste(128, (20, 20, 40, 40))
        guide = Image.new("L", (64, 64), 0); guide.paste(255, (30, 30, 50, 50))
        self.mask = png_bytes(Image.merge("RGB", (prior, guide, Image.new("L", (64, 64), 0))))
        self.data["mask_mode"] = "selection"
        self.data["mask_sha256"] = hashlib.sha256(self.mask).hexdigest().upper()
        result = self.run_handler()
        self.assertEqual(result["maskMode"], "selection")
        self.assertEqual(self.pipeline.selection.getpixel((35, 35)), (128, 255, 0))

    def setUp(self):
        self.image = png_bytes(Image.new("RGB", (64, 64), "blue"))
        mask = Image.new("L", (64, 64), 0)
        mask.paste(255, (20, 20, 40, 40))
        mask.putpixel((19, 25), 128)
        self.mask = png_bytes(mask)
        self.uploads = {}
        self.pipeline = Pipeline()
        self.data = {"operation": "layers", "job_id": "a" * 32,
                     "input_sha256": hashlib.sha256(self.image).hexdigest().upper(), "width": 64, "height": 64}
        for name in ("image", "mask", "background", "foreground", "completion"):
            self.data[name + "_url"] = "https://test.s3.amazonaws.com/" + name + "?X-Amz-Signature=test"
        self.data["mask_sha256"] = hashlib.sha256(self.mask).hexdigest().upper()

    def run_handler(self):
        def get(url, **_):
            return Response(self.mask if "/mask?" in url else self.image)

        def put(url, data, **_):
            self.uploads[url] = data
            return Response()

        with patch.object(handler, "pipeline", self.pipeline), patch.object(handler.requests, "get", get), patch.object(handler.requests, "put", put):
            return handler.handler({"input": self.data})

    def test_manual_alpha_reaches_pipeline_and_completion_binds_its_checksum(self):
        result = self.run_handler()
        self.assertEqual(result["maskSha256"], self.data["mask_sha256"])
        self.assertEqual(self.pipeline.calls[0].getpixel((19, 25)), 128)
        self.assertEqual(self.pipeline.calls[0].getpixel((0, 0)), 0)
        self.assertEqual(json.loads(self.uploads[self.data["completion_url"]]), result)
        self.assertEqual(len(self.uploads), 3)

    def test_automatic_requests_keep_the_previous_contract(self):
        self.data.pop("mask_url")
        self.data.pop("mask_sha256")
        self.assertEqual(self.run_handler()["status"], "completed")
        self.assertEqual(self.pipeline.calls, [None])

    def test_tampered_mask_is_rejected_before_gpu_or_upload(self):
        self.data["mask_sha256"] = "f" * 64
        self.assertIn("error", self.run_handler())
        self.assertEqual(self.pipeline.calls, [])
        self.assertEqual(self.uploads, {})

    def test_mask_cannot_fetch_an_arbitrary_url(self):
        self.data["mask_url"] = "http://localhost/private"
        self.assertIn("error", self.run_handler())
        self.assertEqual(self.pipeline.calls, [])
        self.assertEqual(self.uploads, {})


if __name__ == "__main__":
    unittest.main()
