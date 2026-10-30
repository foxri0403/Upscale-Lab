# Build from backend/ai-worker as the Docker build context:
# docker build -f runpod-see-through/Dockerfile -t upscale-lab-see-through:diagnostics-safetensors .
# Dedicated endpoint; do NOT replace the existing upscaler image.
FROM nvidia/cuda:12.8.1-cudnn-devel-ubuntu24.04
ENV DEBIAN_FRONTEND=noninteractive \
    VIRTUAL_ENV=/opt/venv \
    PATH=/opt/venv/bin:$PATH \
    SEE_THROUGH_REPOSITORY=/opt/see-through \
    UPSCALE_LAB_ADAPTER=/opt/worker/see_through_adapter.py \
    PYTHONUNBUFFERED=1
RUN apt-get update && apt-get install -y --no-install-recommends \
    python3 python3-venv python3-pip git git-lfs libgl1 libglib2.0-0 \
    && rm -rf /var/lib/apt/lists/* \
    && python3 -m venv /opt/venv
RUN pip install --no-cache-dir torch==2.8.0 torchvision==0.23.0 torchaudio==2.8.0 \
    --index-url https://download.pytorch.org/whl/cu128
RUN git clone --depth 1 https://github.com/shitagaki-lab/see-through.git /opt/see-through \
    && cd /opt/see-through \
    && pip install --no-cache-dir -r requirements.txt \
    && ln -s /opt/see-through/common/assets /opt/see-through/assets

# Keep the original targeted LayerDiff/Marigold safetensors patch (v2).
# The patch intentionally fails the image build if upstream files differ.
COPY runpod-see-through/patch_safetensors_loading.py /opt/worker/patch_safetensors_loading.py
RUN python /opt/worker/patch_safetensors_loading.py \
    && python -m py_compile \
       /opt/see-through/common/utils/inference_utils.py \
       /opt/see-through/inference/scripts/inference_psd.py

# Use the newly committed diagnostic handler.
COPY see_through_adapter.py /opt/worker/see_through_adapter.py
COPY generic_quality_refiner.py /opt/worker/generic_quality_refiner.py
COPY runpod-see-through/handler.py /opt/worker/handler.py
COPY runpod-see-through/requirements-worker.txt /opt/worker/requirements-worker.txt
RUN pip install --no-cache-dir -r /opt/worker/requirements-worker.txt
WORKDIR /opt/worker
CMD ["python", "handler.py"]
