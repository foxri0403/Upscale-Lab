import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import handler


class RunPodWorkerTests(unittest.TestCase):
    def test_s3_url_security(self):
        self.assertTrue(handler._check_s3_url("https://bucket.s3.ap-northeast-2.amazonaws.com/key?X-Amz-Signature=abc"))
        for url in (
            "http://bucket.s3.ap-northeast-2.amazonaws.com/key?sign=abc",
            "https://localhost/path?sign=abc",
            "https://example.com/a?sign=abc",
            "https://bucket.s3.ap-northeast-2.amazonaws.com/key",
            "https://attacker@bucket.s3.ap-northeast-2.amazonaws.com/a?sign=abc",
        ):
            with self.subTest(url=url), self.assertRaises(ValueError):
                handler._check_s3_url(url)

    def test_archive_output_and_manifest(self):
        with tempfile.TemporaryDirectory() as tmp:
            directory = Path(tmp) / "out"
            (directory / "layers").mkdir(parents=True)
            (directory / "layers" / "000.png").write_bytes(b"fake-png-test")
            (directory / "manifest.json").write_text(json.dumps({"layers": [{"file": "layers/000.png"}]}))
            archive = Path(tmp) / "parts.zip"
            self.assertEqual(handler._create_output_archive(directory, archive), 1)
            with zipfile.ZipFile(archive) as zipped:
                self.assertEqual(set(zipped.namelist()), {"manifest.json", "layers/000.png"})

    def test_archive_rejects_traversal(self):
        with tempfile.TemporaryDirectory() as tmp:
            directory = Path(tmp) / "out"
            directory.mkdir()
            (directory / "manifest.json").write_text(json.dumps({"layers": [{"file": "layers/../../secret.png"}]}))
            with self.assertRaises(ValueError):
                handler._create_output_archive(directory, Path(tmp) / "result.zip")

    def test_full_handler_protocol_with_mocked_inference(self):
        from PIL import Image
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            repo = root / "official"
            (repo / "inference/scripts").mkdir(parents=True)
            (repo / "inference/scripts/inference_psd.py").write_text("# stub")
            adapter = root / "adapter.py"
            adapter.write_text("# stub")
            sample = root / "sample.png"
            Image.new("RGB", (10, 10), "red").save(sample)
            submitted = {}

            class GetResponse:
                def __enter__(self): return self
                def __exit__(self, *args): return False
                def raise_for_status(self): pass
                def iter_content(self, chunk_size):
                    yield sample.read_bytes()

            class PutResponse:
                def raise_for_status(self): pass

            def fake_process(cmd, **_):
                output = Path(cmd[cmd.index("--output") + 1])
                (output / "layers").mkdir(parents=True)
                Image.new("RGBA", (10, 10), (255, 0, 0, 128)).save(output / "layers/000.png")
                (output / "manifest.json").write_text(json.dumps({"layers": [{"file": "layers/000.png"}]}))

            def fake_put(url, data, **_):
                submitted["zip"] = data.read()
                return PutResponse()

            s3 = "https://bucket.s3.ap-northeast-2.amazonaws.com/"
            event = {"input": {"operation": "character_layers_pretrained",
                               "input_url": s3 + "input?X-Amz-Signature=a",
                               "output_url": s3 + "output?X-Amz-Signature=b"}}
            env = {"SEE_THROUGH_REPOSITORY": str(repo), "UPSCALE_LAB_ADAPTER": str(adapter)}
            with patch.dict(handler.os.environ, env), \
                 patch.object(handler.requests, "get", return_value=GetResponse()), \
                 patch.object(handler.requests, "put", side_effect=fake_put), \
                 patch.object(handler.subprocess, "run", side_effect=fake_process):
                result = handler.handle(event)
            self.assertEqual(result, {"status": "completed", "layer_count": 1})
            with zipfile.ZipFile(io.BytesIO(submitted["zip"])) as zipped:
                self.assertIn("layers/000.png", zipped.namelist())

    def test_does_not_accept_training_operation(self):
        self.assertEqual(handler.handle({"input": {"operation": "train"}})["status"], "failed")

if __name__ == "__main__":
    unittest.main()
