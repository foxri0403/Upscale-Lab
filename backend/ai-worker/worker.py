"""Private LAN GPU HTTP service. Launch with ONE uvicorn worker per GPU."""
from __future__ import annotations

import asyncio
import hmac
import logging
from pathlib import Path
import shutil
import tempfile
from typing import Literal

from fastapi import FastAPI, File, Form, HTTPException, UploadFile
from fastapi.responses import FileResponse, JSONResponse
from starlette.background import BackgroundTask
from worker_config import Settings
from worker_pipeline import ConfigurationError, InputError, process_image

logger = logging.getLogger(__name__)


class BodyTooLarge(Exception):
    pass


class WorkerBoundary:
    """Authenticate and limit requests before multipart parsing/spooling."""
    def __init__(self, app, settings: Settings):
        self.app = app
        self.settings = settings
        self.lock = asyncio.Lock()

    async def __call__(self, scope, receive, send):
        if scope["type"] != "http":
            return await self.app(scope, receive, send)
        headers = dict(scope["headers"])
        key = headers.get(b"x-worker-key", b"")
        if not hmac.compare_digest(key, self.settings.api_key.encode("ascii")):
            return await JSONResponse({"detail": "Unauthorized"}, 401)(scope, receive, send)
        processing = scope["path"] == "/v1/process" and scope["method"] == "POST"
        if not processing:
            return await self.app(scope, receive, send)
        limit = self.settings.max_upload_bytes + 1024 * 1024
        try:
            length = int(headers.get(b"content-length", b"-1"))
        except ValueError:
            length = -1
        if length < 0 or length > limit:
            return await JSONResponse({"detail": "A bounded Content-Length is required"}, 413)(scope, receive, send)
        if self.lock.locked():
            return await JSONResponse({"detail": "GPU is busy; retry later"}, 429)(scope, receive, send)
        async with self.lock:
            received = 0

            async def limited_receive():
                nonlocal received
                message = await receive()
                received += len(message.get("body", b""))
                if received > limit:
                    raise BodyTooLarge()
                return message
            try:
                await self.app(scope, limited_receive, send)
            except BodyTooLarge:
                await JSONResponse({"detail": "Upload limit exceeded"}, 413)(scope, receive, send)


def create_app(settings: Settings | None = None, runner=process_image) -> FastAPI:
    settings = settings or Settings.from_env()
    if len(settings.api_key) < 32 or any(ord(c) < 33 or ord(c) > 126 for c in settings.api_key):
        raise RuntimeError("Set UPSCALE_WORKER_API_KEY to at least 32 printable non-space ASCII characters")
    app = FastAPI(title="Upscale Lab local GPU worker", docs_url=None, redoc_url=None, openapi_url=None)
    app.add_middleware(WorkerBoundary, settings=settings)

    @app.get("/health")
    async def health():
        return {"status": "ok", "configuredCapabilities": settings.capabilities(), "gpuVerified": False}

    @app.post("/v1/process")
    async def process(file: UploadFile = File(...),
                      operation: Literal["upscale", "layers", "upscale_layers"] = Form(...),
                      engine: Literal["see-through", "partglee"] = Form("see-through"),
                      scale: int = Form(2)):
        if scale not in (2, 4):
            raise HTTPException(422, "Scale must be 2 or 4")
        settings.work_dir.mkdir(parents=True, exist_ok=True)
        workspace = Path(tempfile.mkdtemp(prefix="job-", dir=settings.work_dir)).resolve()
        handed_off = False
        try:
            size = 0
            with (workspace / "upload").open("wb") as destination:
                while chunk := await file.read(1024 * 1024):
                    size += len(chunk)
                    if size > settings.max_upload_bytes:
                        raise HTTPException(413, "Upload limit exceeded")
                    destination.write(chunk)
            if size == 0:
                raise HTTPException(422, "Empty image")
            result = await runner(settings, workspace, operation, engine, scale)
            response = FileResponse(result, media_type="application/zip", filename="result.zip",
                                    background=BackgroundTask(shutil.rmtree, workspace, ignore_errors=True))
            handed_off = True
            return response
        except InputError as error:
            raise HTTPException(422, str(error)) from error
        except ConfigurationError as error:
            logger.error("Worker configuration: %s", error)
            raise HTTPException(503, "Model environment is not configured; check worker logs") from error
        except TimeoutError as error:
            raise HTTPException(504, "Model processing timeout") from error
        except HTTPException:
            raise
        except Exception as error:
            logger.exception("GPU inference failed")
            raise HTTPException(500, "Model processing failed; check worker logs") from error
        finally:
            await file.close()
            if not handed_off:
                shutil.rmtree(workspace, ignore_errors=True)

    return app
