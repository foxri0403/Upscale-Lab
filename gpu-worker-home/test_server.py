import json
import threading
import time
import unittest
from urllib.error import HTTPError
from urllib.request import Request, urlopen

from server import JobQueue, PENDING, create_server


class HomeGpuContractTests(unittest.TestCase):
    def setUp(self):
        self.key = "test-worker-key-long-enough-for-the-api"
        self.started = threading.Event()
        self.release = threading.Event()
        self.processed = []
        self.jobs = JobQueue({"local-upscale": ("upscale_s3", self.work)})
        self.server = create_server(0, self.key, "test GPU", self.jobs)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.base_url = f"http://127.0.0.1:{self.server.server_port}"

    def tearDown(self):
        self.release.set()
        self.server.shutdown()
        self.server.server_close()
        self.thread.join()
        self.jobs.executor.shutdown(cancel_futures=True)

    def work(self, data, check):
        self.started.set()
        if not self.release.wait(3):
            raise RuntimeError("Test did not release the first job")
        check()
        self.processed.append(data["target_long_edge"])
        return {"status": "completed", "transfer": "s3", "width": data["target_long_edge"], "height": 1080}

    def request(self, path, body=None, authorized=True):
        headers = {"Content-Type": "application/json"}
        if authorized:
            headers["Authorization"] = f"Bearer {self.key}"
        data = json.dumps(body).encode() if body is not None else None
        with urlopen(Request(self.base_url + path, data=data, headers=headers), timeout=3) as response:
            return json.load(response)

    def submit(self, target):
        return self.request("/v2/local-upscale/run", {"input": {"operation": "upscale_s3", "target_long_edge": target}})

    def test_authenticated_submission_returns_the_existing_s3_result_contract(self):
        with self.assertRaises(HTTPError) as unauthorized:
            self.request("/health", authorized=False)
        self.assertEqual(401, unauthorized.exception.code)
        self.assertEqual(["local-upscale"], self.request("/health")["endpoints"])
        with self.assertRaises(HTTPError) as invalid:
            self.request("/v2/local-upscale/run", {"input": {"operation": "layers"}})
        self.assertEqual(400, invalid.exception.code)

        job = self.submit(1920)
        self.assertEqual("IN_QUEUE", job["status"])
        status_path = f"/v2/local-upscale/status/{job['id']}"
        self.release.set()
        for _ in range(100):
            job = self.request(status_path)
            if job["status"] not in PENDING:
                break
            time.sleep(.01)
        self.assertEqual("COMPLETED", job["status"])
        self.assertEqual({"status": "completed", "transfer": "s3", "width": 1920, "height": 1080}, job["output"])

    def test_cancelling_a_queued_job_does_not_start_a_second_gpu_inference(self):
        self.submit(1920)
        self.assertTrue(self.started.wait(2))
        job = self.submit(2560)
        cancelled = self.request(f"/v2/local-upscale/cancel/{job['id']}", {})
        self.assertEqual("CANCELLED", cancelled["status"])
        self.release.set()
        self.jobs.executor.shutdown()
        self.assertEqual([1920], self.processed)
        self.assertEqual(cancelled, self.request(f"/v2/local-upscale/status/{job['id']}"))
