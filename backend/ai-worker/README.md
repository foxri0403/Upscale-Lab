# Local GPU worker

Separate LAN GPU service for the existing .NET backend. See [setup, model requirements, API examples and limitations](../../docs/local-gpu.md).

| File | Responsibility |
|---|---|
| `worker.py` | Authenticated multipart HTTP boundary, request size limit, one GPU job at a time |
| `worker_pipeline.py` | Image normalization, separate model processes, deadline/tree termination, bounded ZIP packaging |
| `worker_config.py` | GPU-host-only environment settings (`UPSCALE_WORKER_*`) |
| `realesrgan_adapter.py` | Local Real-ESRGAN x4plus weights, 2x/4x output, tiled CUDA inference |
| `partglee_adapter.py` | PartGLEE PACO instance/part masks, approximate depth, inpainted background, full-canvas PNGs |
| `see_through_adapter.py` | Official See-through PSD command and full-canvas PNG export |
| `layer_export.py` | Renderer-compatible manifest metadata |

Use Python 3.12 for the HTTP host and **independent upstream environments for each model**. Install only HTTP dependencies in the worker environment:

```bash
python3.12 -m venv .venv
.venv/bin/python -m pip install -r requirements.txt
# Export API key and model paths first; see .env.example (not auto-loaded).
.venv/bin/python -m uvicorn worker:create_app --factory --host 0.0.0.0 --port 8090 --workers 1
```

All requests require `X-Worker-Key`. `GET /health` reports configured capabilities, not successful model inference. `POST /v1/process` requires multipart `file`, `operation=upscale|layers|upscale_layers`, `engine=partglee|see-through` (default see-through), `scale=2|4` (default 2). Response is `application/zip` with `manifest.json` and PNGs. Client-supplied URLs/paths/commands are never executed.

Manifest schema version 1 uses `operation`, `engine`, `width`, `height`, `layers`. Each layer has `file`, `layerType`, `layerOrder`, `depth`, `positionX`, `positionY`, `rotation`, `scale`, `movementX`, `movementY`. Upscale results also include `imageFile`, `imageWidth`, `imageHeight`. In combined mode these can differ from the layer canvas size because layer engines have their own resolution limits. Layer order is back-to-front and all layer PNGs cover the whole canvas.

CPU-only tests (no model weights):

```bash
.venv/bin/python -m pip install -r requirements-test.txt
.venv/bin/python -m unittest discover -s tests -v
```

PartGLEE segmentation is converted to approximate parallax, not true geometry or hidden-surface reconstruction. No fake inference fallback is used. Actual CUDA/model compatibility, quality and performance need a smoke test on the GPU host. See-through's group offload is available through `UPSCALE_WORKER_SEE_THROUGH_GROUP_OFFLOAD=true`; quantized variants are not selected by this adapter. Models/sources are external and must be used under their upstream licenses.

Legacy mode still allows the .NET API host to call `see_through_adapter.py` directly when `LocalGpu:Enabled=false` and `SeeThrough` is configured.
