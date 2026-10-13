import asyncio
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

import httpx
import numpy as np
from PIL import Image

from worker import create_app
from worker_config import Settings
from worker_pipeline import InputError, normalize_image, package_result, process_image
from partglee_adapter import export_layers


def png(size=(16, 16)):
    stream = io.BytesIO()
    Image.new("RGB", size, "red").save(stream, "PNG")
    return stream.getvalue()


class WorkerTests(unittest.IsolatedAsyncioTestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.settings = Settings(api_key="k" * 32, work_dir=self.root / "jobs")
        self.headers = {"X-Worker-Key": self.settings.api_key}

    def tearDown(self):
        self.temp.cleanup()

    def client(self, runner=process_image):
        app = create_app(self.settings, runner=runner)
        return httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://worker", headers=self.headers)

    async def test_authentication_precedes_request_parsing(self):
        async with self.client() as client:
            result = await client.post("/v1/process", headers={"X-Worker-Key": "wrong"}, content=b"not multipart")
        self.assertEqual(result.status_code, 401)
        self.assertFalse(self.settings.work_dir.exists())

    async def test_invalid_options_and_image_fail_without_model_execution(self):
        async with self.client() as client:
            for data in ({"operation": "shell"}, {"operation": "upscale", "scale": "3"}):
                response = await client.post("/v1/process", data=data, files={"file": ("input.png", png())})
                self.assertEqual(response.status_code, 422)
            response = await client.post("/v1/process", data={"operation": "upscale"}, files={"file": ("bad.png", b"bad")})
            self.assertEqual(response.status_code, 422)
        self.assertEqual(list(self.settings.work_dir.iterdir()), [])

    async def test_missing_model_is_explicit_configuration_failure(self):
        async with self.client() as client:
            result = await client.post("/v1/process", data={"operation": "upscale"}, files={"file": ("test.png", png())})
        self.assertEqual(result.status_code, 503)
        self.assertEqual(list(self.settings.work_dir.iterdir()), [])

    async def test_oversized_request_is_rejected_before_spooling(self):
        async with self.client() as client:
            result = await client.post("/v1/process", headers={"Content-Length": str(100 * 1024 * 1024)}, content=b"x")
        self.assertEqual(result.status_code, 413)
        self.assertFalse(self.settings.work_dir.exists())

    async def test_busy_gpu_rejects_second_request_and_returns_zip_then_cleans(self):
        entered, release = asyncio.Event(), asyncio.Event()

        async def fake_runner(settings, workspace, operation, engine, scale):
            self.assertEqual((operation, engine, scale), ("upscale", "partglee", 4))
            entered.set()
            await release.wait()
            result = workspace / "result.zip"
            result.write_bytes(b"fake-result-for-transport-test")
            return result

        async with self.client(fake_runner) as client:
            args = {"data": {"operation": "upscale", "engine": "partglee", "scale": "4"}, "files": {"file": ("test.png", png())}}
            first = asyncio.create_task(client.post("/v1/process", **args))
            await asyncio.wait_for(entered.wait(), 3)
            second = await client.post("/v1/process", **args)
            self.assertEqual(second.status_code, 429)
            release.set()
            response = await first
            self.assertEqual(response.status_code, 200)
            self.assertEqual(response.headers["content-type"], "application/zip")
        self.assertEqual(list(self.settings.work_dir.iterdir()), [])

    async def test_timeout_returns_504_and_cleans_workspace(self):
        async def timeout(*args):
            raise TimeoutError()
        async with self.client(timeout) as client:
            result = await client.post("/v1/process", data={"operation": "upscale"}, files={"file": ("test.png", png())})
        self.assertEqual(result.status_code, 504)
        self.assertEqual(list(self.settings.work_dir.iterdir()), [])

    async def test_health_reports_configuration_not_claimed_gpu_readiness(self):
        async with self.client() as client:
            result = await client.get("/health")
        self.assertFalse(result.json()["gpuVerified"])
        self.assertFalse(result.json()["configuredCapabilities"]["upscale"])

    def test_normalizer_enforces_pixel_limit(self):
        (self.root / "upload").write_bytes(png())
        with self.assertRaises(InputError):
            normalize_image(self.root / "upload", self.root / "input.png", Settings(max_input_pixels=10))

    def test_part_masks_export_full_canvas_and_approximation_metadata(self):
        rgb = np.full((32, 32, 3), 100, dtype=np.uint8)
        masks = np.zeros((2, 32, 32), dtype=bool)
        masks[0, 4:28, 4:28] = True
        masks[1, 8:20, 8:20] = True
        export_layers(rgb, masks, [0.9, 0.8], ["body", "face"], self.root, 0.5)
        manifest = json.loads((self.root / "manifest.json").read_text())
        self.assertEqual(manifest["depthMethod"], "vertical-position-heuristic")
        self.assertEqual(len(manifest["layers"]), 3)
        composite = Image.new("RGBA", (32, 32))
        for layer in manifest["layers"]:
            with Image.open(self.root / layer["file"]) as image:
                self.assertEqual(image.size, (32, 32))
                composite.alpha_composite(image.convert("RGBA"))
        np.testing.assert_array_equal(np.asarray(composite)[:, :, :3], rgb)

    def test_empty_part_detection_does_not_fake_success(self):
        with self.assertRaises(RuntimeError):
            export_layers(np.zeros((16, 16, 3), dtype=np.uint8), [], [], [], self.root, 0.5)

    def test_packager_rejects_escaping_model_output(self):
        output = self.root / "output"
        output.mkdir()
        (output / "manifest.json").write_text("{}")
        (self.root / "outside.png").write_bytes(png())
        manifest = {"operation": "layers", "width": 16, "height": 16, "layers": [{"file": "../outside.png"}]}
        with self.assertRaises(RuntimeError):
            package_result(output, self.root / "result.zip", manifest, self.settings)

    async def test_combined_pipeline_uses_upscaled_image_as_layer_input(self):
        repo = self.root / "repo"
        (repo / "realesrgan").mkdir(parents=True)
        (repo / "realesrgan/__init__.py").touch()
        (repo / "inference/scripts").mkdir(parents=True)
        (repo / "inference/scripts/inference_psd.py").touch()
        weights = self.root / "weights.pth"
        weights.touch()
        settings = Settings(realesrgan_repository=str(repo), realesrgan_weights=str(weights), see_through_repository=str(repo))
        workspace = self.root / "work"
        workspace.mkdir()
        (workspace / "upload").write_bytes(png())
        calls = []

        async def run(command, cwd, log):
            calls.append(command)
            destination = Path(command[command.index("--output") + 1])
            if "realesrgan_adapter.py" in command[1]:
                destination.write_bytes(png((32, 32)))
            else:
                self.assertEqual(Path(command[command.index("--input") + 1]).name, "upscaled.png")
                (destination / "layer.png").write_bytes(png())
                (destination / "manifest.json").write_text(json.dumps({"schemaVersion": 1, "width": 16, "height": 16, "layers": [{"file": "layer.png"}]}))

        with patch("worker_pipeline.run_model", side_effect=run):
            archive = await process_image(settings, workspace, "upscale_layers", "see-through", 2)
        with zipfile.ZipFile(archive) as result:
            manifest = json.loads(result.read("manifest.json"))
            self.assertEqual(manifest["imageWidth"], 32)
            self.assertEqual(manifest["width"], 16)
            self.assertEqual(manifest["operation"], "upscale_layers")
        self.assertEqual(len(calls), 2)


if __name__ == "__main__":
    unittest.main()
