# Local GPU workstation handoff

Last updated: 2026-10-15 (Asia/Seoul)

This file records the state of the school Windows workstation used to continue
`codex/ui-local-gpu-integration`. It is a machine-specific continuation note,
not a deployment guide. The general setup and API contract remain in
[local-gpu.md](local-gpu.md).

## Stop state

Work was deliberately stopped at the user's request. There are no active
`uvicorn`, See-through inference, Hugging Face model download, or PartGLEE
PyTorch download processes. Do not assume port 8090 is listening after a new
session or WSL restart.

The Git branch at handoff is `codex/ui-local-gpu-integration`. External model
repositories are checked out at the documented immutable refs:

| Repository | Path in WSL | Commit |
|---|---|---|
| Real-ESRGAN | `/home/wsluser/upscale-lab-gpu/models/Real-ESRGAN` | `a4abfb2979a7bbff3f69f58f58ae324608821e27` |
| PartGLEE | `/home/wsluser/upscale-lab-gpu/models/PartGLEE` | `8c6b83e0be076e8fe640943096059ee5c180de0e` |
| See-through | `/home/wsluser/upscale-lab-gpu/models/see-through` | `df019de5129d6c4b406587a14c3501669441a783` |

## Machine and common paths

- Windows host with WSL2 Ubuntu 26.04.1.
- GPU: NVIDIA RTX A4000, 16,376 MiB VRAM.
- NVIDIA driver: 597.06. CUDA is visible inside WSL.
- WSL filesystem had about 916 GB free when work stopped.
- Common root: `/home/wsluser/upscale-lab-gpu`.
- Model environments: `/home/wsluser/upscale-lab-gpu/envs`.
- Model repositories: `/home/wsluser/upscale-lab-gpu/models`.
- Worker jobs: `/home/wsluser/upscale-lab-gpu/jobs`.
- HTTP worker environment: `/home/wsluser/upscale-lab-gpu/envs/worker`.
- `uv`: `/home/wsluser/.local/bin/uv` (`0.13.1`).
- `micromamba`: `/home/wsluser/.local/bin/micromamba` (`2.9.0`).
- Windows .NET SDK 8.0.426 and Python 3.12.10 were installed with `winget`.

The local .NET user-secret store already contains `LocalGpu:Enabled=true`,
`LocalGpu:BaseUrl=http://localhost:8090`, `LocalGpu:ApiKey`, and
`LocalGpu:TimeoutMinutes=35`. Never copy the shared API key into this document
or Git. If it must be rotated, update both the worker process environment and
the .NET user-secret store.

## School network CA

The school network performs TLS inspection with the following root:

- Subject: `Plantynet OfficeGuarad CA_V3`
- Windows root certificate thumbprint:
  `47A677A1828522E2EF3C28876D7E7482BB164A9D`
- WSL PEM: `/home/wsluser/upscale-lab-gpu/certs/plantynet-officeguard.pem`
- WSL combined CA bundle: `/home/wsluser/upscale-lab-gpu/certs/ca-bundle.pem`

The combined bundle was validated with an HTTP 200 response from
`https://huggingface.co`. Prefer setting the following variables instead of
disabling TLS verification:

```bash
export SSL_CERT_FILE=/home/wsluser/upscale-lab-gpu/certs/ca-bundle.pem
export REQUESTS_CA_BUNDLE=/home/wsluser/upscale-lab-gpu/certs/ca-bundle.pem
```

The DER export under `backend/ai-worker/workspace/` is ignored by Git and is
not required while the WSL PEM and bundle remain present.

## Real-ESRGAN: complete and verified

Environment: `/home/wsluser/upscale-lab-gpu/envs/realesrgan`

- Python 3.12.15.
- PyTorch `2.7.1+cu128`; `torch.cuda.is_available()` returned `True`.
- Official `RealESRGAN_x4plus.pth` is at
  `/home/wsluser/upscale-lab-gpu/models/Real-ESRGAN/weights/RealESRGAN_x4plus.pth`.
- Weight SHA-256:
  `4fa0d38905f75ac06eb49a7951b426670021be3018265fd191d2125df9d682f1`.
- A direct adapter smoke test produced a 256x192 image from a 128x96 input.
- An authenticated HTTP `POST /v1/process` smoke test returned a ZIP containing
  `manifest.json` and `upscaled.png`; the extracted image was 256x192 RGBA.

`backend/ai-worker/realesrgan_adapter.py` contains a compatibility shim for
BasicSR 1.4.2's removed `torchvision.transforms.functional_tensor` import.

## See-through: environment complete, model download incomplete

Environment: `/home/wsluser/upscale-lab-gpu/envs/see-through`

- Python 3.12.15.
- PyTorch `2.8.0+cu128`; CUDA 12.8 and RTX A4000 were verified.
- Official `requirements.txt` installation completed.
- Verified imports include `diffusers==0.37.0`, `transformers==5.0.0`, and
  `psd-tools==1.14.2`.
- Repository `assets` symlink points to `common/assets`.
- Hugging Face cache: `/home/wsluser/upscale-lab-gpu/hf-cache`.
- Cache size at stop: approximately 2.5 GB.
- The persistent incomplete LayerDiff blob was approximately 2,422,210,560
  bytes. Hugging Face should resume it on the next identical request.
- Actual inference had reached `TransparentVAE.from_pretrained`; the only first
  failure was TLS trust, which is now fixed by the CA bundle.

Resume the official-sample smoke test with:

```bash
cd /home/wsluser/upscale-lab-gpu/models/see-through
export SSL_CERT_FILE=/home/wsluser/upscale-lab-gpu/certs/ca-bundle.pem
export REQUESTS_CA_BUNDLE=/home/wsluser/upscale-lab-gpu/certs/ca-bundle.pem
export HF_HOME=/home/wsluser/upscale-lab-gpu/hf-cache
export HF_HUB_DISABLE_XET=1

/home/wsluser/upscale-lab-gpu/envs/see-through/bin/python \
  /mnt/c/2224029/Upscale-Lab/backend/ai-worker/see_through_adapter.py \
  --repository /home/wsluser/upscale-lab-gpu/models/see-through \
  --input /home/wsluser/upscale-lab-gpu/models/see-through/common/assets/test_image.png \
  --output /home/wsluser/upscale-lab-gpu/see-through-smoke \
  --resolution 512 \
  --group-offload
```

Success criteria: the adapter exits zero, creates `manifest.json`, and exports
one or more full-canvas PNG files under `see-through-smoke/layers`. Inspect the
PSD-derived manifest and image dimensions before enabling the capability in the
HTTP worker. The first run still needs the remaining LayerDiff and Marigold
model downloads and can take a long time without an `HF_TOKEN`.

## PartGLEE: compiler environment complete, PyTorch incomplete

Environment: `/home/wsluser/upscale-lab-gpu/envs/partglee`

Completed:

- Python 3.9.23.
- GCC/G++ 11.4.0 from conda-forge.
- CUDA NVCC 12.1 (`Build cuda_12.1.r12.1/compiler.32415258_0`).
- Ninja and CMake.
- PACO inference config exists at
  `projects/PartGLEE/configs/Inference/RN50/paco.yaml`.
- CLIP JSON/tokenizer configuration files already exist in
  `projects/PartGLEE/clip_vit_base_patch32/`.

The 2.2 GB PyTorch 2.1 wheel download was intentionally interrupted at about
1.2 GB. Pip's temporary file is not a supported persistent cache; restart the
installation from the beginning:

```bash
export SSL_CERT_FILE=/home/wsluser/upscale-lab-gpu/certs/ca-bundle.pem

/home/wsluser/upscale-lab-gpu/envs/partglee/bin/python -m pip install \
  torch==2.1.0+cu121 torchvision==0.16.0+cu121 \
  --index-url https://download.pytorch.org/whl/cu121 \
  --trusted-host download.pytorch.org \
  --trusted-host download-r2.pytorch.org \
  --trusted-host pypi.org \
  --trusted-host files.pythonhosted.org
```

After PyTorch succeeds, continue from the upstream installation guide. Keep
versions aligned with PyTorch 2.1 and do not let an unpinned `xformers` upgrade
PyTorch:

```bash
PGPY=/home/wsluser/upscale-lab-gpu/envs/partglee/bin/python
$PGPY -m pip install \
  shapely==1.7.1 numpy==1.26.4 Pillow==8.4.0 lvis scipy fairscale einops \
  xformers==0.0.22.post7 tensorboard opencv-python-headless timm ftfy \
  transformers==4.36.0 gdown \
  --trusted-host pypi.org --trusted-host files.pythonhosted.org

cd /home/wsluser/upscale-lab-gpu/models/PartGLEE
export CUDA_HOME=/home/wsluser/upscale-lab-gpu/envs/partglee
export CC=/home/wsluser/upscale-lab-gpu/envs/partglee/bin/x86_64-conda-linux-gnu-gcc
export CXX=/home/wsluser/upscale-lab-gpu/envs/partglee/bin/x86_64-conda-linux-gnu-g++
export PATH="$CUDA_HOME/bin:$PATH"

$PGPY -m pip install -e . --no-build-isolation
$PGPY -m pip install \
  'git+https://github.com/wjf5203/cocoapi.git#egg=pycocotools&subdirectory=PythonAPI'

cd projects/PartGLEE/partglee/models/pixel_decoder/ops
$PGPY setup.py build install
```

Then download the two missing model artifacts:

1. Official CLIP weight:
   `https://huggingface.co/spaces/Junfeng5/GLEE_demo/resolve/main/GLEE/clip_vit_base_patch32/pytorch_model.bin`
   into `projects/PartGLEE/clip_vit_base_patch32/`.
2. Official RN50 checkpoint, Google Drive ID
   `1SK0HsYXBcTvprFAxqxH-lgpkgV67B4UZ`, into
   `projects/PartGLEE/checkpoint/PartGLEE_RN50.pth`.

Verify CUDA imports and run the adapter against a representative photograph:

```bash
cd /home/wsluser/upscale-lab-gpu/models/PartGLEE
$PGPY /mnt/c/2224029/Upscale-Lab/backend/ai-worker/partglee_adapter.py \
  --input /path/to/photo.png \
  --output /home/wsluser/upscale-lab-gpu/partglee-smoke \
  --config projects/PartGLEE/configs/Inference/RN50/paco.yaml \
  --weights projects/PartGLEE/checkpoint/PartGLEE_RN50.pth \
  --threshold 0.5 \
  --max-dimension 1280
```

Success criteria: a manifest, inpainted background, and at least one detected
foreground layer. A synthetic geometric sample is not suitable because the
adapter intentionally fails when PACO produces no usable detections.

## Restart the HTTP worker after both engines pass

Start from `backend/ai-worker` in WSL and provide the existing shared API key
through the environment. Do not store it in Git.

```bash
cd /mnt/c/2224029/Upscale-Lab/backend/ai-worker
export UPSCALE_WORKER_API_KEY='<same value as the .NET user secret>'
export UPSCALE_WORKER_WORK_DIR=/home/wsluser/upscale-lab-gpu/jobs
export UPSCALE_WORKER_REALESRGAN_REPOSITORY=/home/wsluser/upscale-lab-gpu/models/Real-ESRGAN
export UPSCALE_WORKER_REALESRGAN_PYTHON=/home/wsluser/upscale-lab-gpu/envs/realesrgan/bin/python
export UPSCALE_WORKER_REALESRGAN_WEIGHTS=/home/wsluser/upscale-lab-gpu/models/Real-ESRGAN/weights/RealESRGAN_x4plus.pth
export UPSCALE_WORKER_PARTGLEE_REPOSITORY=/home/wsluser/upscale-lab-gpu/models/PartGLEE
export UPSCALE_WORKER_PARTGLEE_PYTHON=/home/wsluser/upscale-lab-gpu/envs/partglee/bin/python
export UPSCALE_WORKER_PARTGLEE_WEIGHTS=/home/wsluser/upscale-lab-gpu/models/PartGLEE/projects/PartGLEE/checkpoint/PartGLEE_RN50.pth
export UPSCALE_WORKER_SEE_THROUGH_REPOSITORY=/home/wsluser/upscale-lab-gpu/models/see-through
export UPSCALE_WORKER_SEE_THROUGH_PYTHON=/home/wsluser/upscale-lab-gpu/envs/see-through/bin/python
export UPSCALE_WORKER_SEE_THROUGH_GROUP_OFFLOAD=true
export SSL_CERT_FILE=/home/wsluser/upscale-lab-gpu/certs/ca-bundle.pem
export REQUESTS_CA_BUNDLE=/home/wsluser/upscale-lab-gpu/certs/ca-bundle.pem
export HF_HOME=/home/wsluser/upscale-lab-gpu/hf-cache
export HF_HUB_DISABLE_XET=1

/home/wsluser/upscale-lab-gpu/envs/worker/bin/python -m uvicorn \
  worker:create_app --factory --host 0.0.0.0 --port 8090 --workers 1
```

Check `/health`, then make separate authenticated `layers` requests for
`engine=partglee` and `engine=see-through`. `/health` only reports configuration;
successful image requests are the required GPU verification.

## Completed repository validation

Before the engine continuation work:

- Python worker tests: 12 passed.
- .NET tests: 77 passed.
- `git diff --check`: passed.
- The Real-ESRGAN compatibility change had no editor diagnostics.

Re-run the focused Python tests and .NET tests after any additional adapter or
worker changes.
