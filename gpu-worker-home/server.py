import argparse
import hmac
import json
import os
import re
import threading
import time
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass, field
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from uuid import uuid4

from http_body import BodyTooLarge, read_http_body


PORT = 18189
KEY_FILE = Path(__file__).with_name("home-gpu.key")
PENDING = {"IN_QUEUE", "IN_PROGRESS"}
MAX_ACTIVE_JOBS = 8
MAX_RETAINED_JOBS = 512
RESULT_RETENTION_SECONDS = 2 * 60 * 60


@dataclass
class Job:
    endpoint: str
    deadline: float
    execution_seconds: float
    id: str = field(default_factory=lambda: uuid4().hex)
    status: str = "IN_QUEUE"
    output: dict | None = None
    finished_at: float | None = None

    def snapshot(self):
        result = {"id": self.id, "status": self.status}
        if self.output is not None:
            result["output"] = self.output
        return result


class JobQueue:
    def __init__(self, routes):
        self.routes = routes
        self.jobs = {}
        self.lock = threading.Lock()
        self.executor = ThreadPoolExecutor(max_workers=1, thread_name_prefix="cuda")

    def submit(self, endpoint, data, policy):
        now = time.monotonic()
        ttl = policy.get("ttl", 1_800_000)
        execution = policy.get("executionTimeout", ttl)
        if any(type(value) is not int or not 1000 <= value <= 3_600_000 for value in (ttl, execution)):
            raise ValueError("Invalid job deadline.")

        with self.lock:
            completed = sorted((job for job in self.jobs.values() if job.finished_at is not None),
                               key=lambda job: job.finished_at)
            if len(self.jobs) - len(completed) >= MAX_ACTIVE_JOBS:
                return None
            for job in completed:
                if len(self.jobs) < MAX_RETAINED_JOBS and now - job.finished_at < RESULT_RETENTION_SECONDS:
                    break
                del self.jobs[job.id]
            job = Job(endpoint, now + ttl / 1000, execution / 1000)
            self.jobs[job.id] = job
            self.executor.submit(self.run, job, data)
            return job.snapshot()

    def update_timeout(self, job):
        if job.status in PENDING and time.monotonic() >= job.deadline:
            job.status = "TIMED_OUT"

    def run(self, job, data):
        def check():
            with self.lock:
                self.update_timeout(job)
                if job.status != "IN_PROGRESS":
                    raise InterruptedError()

        try:
            with self.lock:
                self.update_timeout(job)
                if job.status != "IN_QUEUE":
                    return
                job.status = "IN_PROGRESS"
                job.deadline = min(job.deadline, time.monotonic() + job.execution_seconds)

            _, run = self.routes[job.endpoint]
            output = run(data, check)
            with self.lock:
                self.update_timeout(job)
                if job.status == "IN_PROGRESS":
                    job.status = "COMPLETED" if output.get("status") == "completed" else "FAILED"
                    if job.status == "COMPLETED":
                        job.output = output
        except InterruptedError:
            pass
        except Exception as error:
            # URLs, request bodies and bearer credentials do not belong in worker logs.
            print(f"{job.endpoint} job {job.id} failed ({type(error).__name__}).", flush=True)
        finally:
            with self.lock:
                self.update_timeout(job)
                if job.status in PENDING:
                    job.status = "FAILED"
                job.finished_at = time.monotonic()

    def status(self, endpoint, job_id, cancel=False):
        with self.lock:
            job = self.jobs.get(job_id)
            if job is None or job.endpoint != endpoint:
                return None
            self.update_timeout(job)
            if cancel and job.status in PENDING:
                job.status = "CANCELLED"
            return job.snapshot()

    def close(self):
        with self.lock:
            for job in self.jobs.values():
                if job.status in PENDING:
                    job.status = "CANCELLED"
        self.executor.shutdown(cancel_futures=True)


def create_server(port, key, gpu, jobs):
    expected_auth = f"Bearer {key}".encode()

    class Handler(BaseHTTPRequestHandler):
        def setup(self):
            self.request.settimeout(10)
            super().setup()

        def log_message(self, format, *args):
            pass

        def reply(self, status, result):
            payload = json.dumps(result).encode()
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(payload)))
            self.end_headers()
            self.wfile.write(payload)

        def do_GET(self):
            self.dispatch()

        def do_POST(self):
            self.dispatch()

        def dispatch(self):
            auth = self.headers.get("Authorization", "").encode()
            if not hmac.compare_digest(auth, expected_auth):
                self.reply(401, {"error": "Unauthorized"})
                return
            if self.command == "GET" and self.path == "/health":
                self.reply(200, {"status": "ready", "gpu": gpu, "endpoints": list(jobs.routes)})
                return
            parts = self.path.split("/")
            if len(parts) >= 4 and parts[:2] == ["", "v2"] and parts[2] in jobs.routes:
                endpoint, action = parts[2:4]
                if self.command == "POST" and action == "run" and len(parts) == 4:
                    self.submit(endpoint)
                    return
                if len(parts) == 5 and (self.command, action) in (("GET", "status"), ("POST", "cancel")):
                    result = jobs.status(endpoint, parts[4], cancel=action == "cancel")
                    if result is not None:
                        self.reply(200, result)
                        return
            self.reply(404, {"error": "Not found"})

        def submit(self, endpoint):
            try:
                body = read_http_body(self.headers, self.rfile)
                if not body:
                    self.reply(413, {"error": "Request is too large or empty"})
                    return
                payload = json.loads(body)
                if not isinstance(payload, dict):
                    raise ValueError()
                data, policy = payload.get("input"), payload.get("policy", {})
                operation, _ = jobs.routes[endpoint]
                if not isinstance(data, dict) or data.get("operation") != operation or not isinstance(policy, dict):
                    raise ValueError()
                result = jobs.submit(endpoint, data, policy)
            except BodyTooLarge:
                self.reply(413, {"error": "Request is too large or empty"})
                return
            except (ValueError, TimeoutError):
                self.reply(400, {"error": "Invalid job request"})
                return
            if result:
                self.reply(200, result)
            else:
                self.reply(429, {"error": "GPU queue is full"})

    return ThreadingHTTPServer(("127.0.0.1", port), Handler)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--layers", action="store_true")
    parser.add_argument("--character-root", type=Path)
    parser.add_argument("--character-models", type=Path)
    args = parser.parse_args()
    if bool(args.character_root) != bool(args.character_models):
        parser.error("See-through runtime and model directories must be supplied together.")
    key = KEY_FILE.read_text(encoding="ascii").strip()
    if not re.fullmatch(r"[!-~]{32,}", key):
        raise RuntimeError("Invalid home GPU bearer key.")
    os.environ["HF_HUB_OFFLINE"] = "1"
    os.environ["TRANSFORMERS_OFFLINE"] = "1"
    os.environ.setdefault("HF_HOME", str(KEY_FILE.parent / ".venv" / "huggingface"))
    import inference
    import torch

    if not torch.cuda.is_available():
        raise RuntimeError("An NVIDIA CUDA GPU is required.")
    torch.empty(1, device="cuda").fill_(1)
    torch.cuda.synchronize()
    gpu = torch.cuda.get_device_name()
    routes = {"local-upscale": ("upscale_s3", inference.upscale)}
    if args.layers:
        inference.prepare_layers()
        routes["local-layers"] = ("layers", inference.layers)
    if args.character_root:
        from character import create_processor

        routes["local-character"] = ("character_layers_pretrained",
                                     create_processor(args.character_root, args.character_models))
    jobs = JobQueue(routes)
    with create_server(PORT, key, gpu, jobs) as server:
        print(f"Home GPU worker at http://127.0.0.1:{PORT} using {gpu}: {', '.join(routes)}", flush=True)
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass
        finally:
            jobs.close()


if __name__ == "__main__":
    main()
