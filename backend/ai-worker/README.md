# See-through adapter

LiveLayer keeps the existing ASP.NET Core API and runs AI inference as an external Python process. The adapter calls the official See-through entry point, `inference/scripts/inference_psd.py`, and exports each visible PSD pixel layer as PNG plus `manifest.json`.

## Setup

1. Clone `https://github.com/shitagaki-lab/see-through` outside this repository.
2. Follow its README to create a Python 3.12 environment, install the matching CUDA/PyTorch build and `requirements.txt`, and make the `assets` link.
3. Verify the official command directly with a test image.
4. Configure `SeeThrough__RepositoryPath`, `SeeThrough__PythonExecutable`, and the absolute `SeeThrough__AdapterScriptPath`.
5. Set `SeeThrough__Enabled=true` only on a host with the required models and GPU resources.

The upstream default pipeline needs roughly 12–16 GB of VRAM at 1280 resolution. Its documented group-offload and quantized variants require upstream-specific command changes; this adapter intentionally uses the standard pipeline until an operational profile is chosen and tested.

No model weights or See-through source files are committed here.
