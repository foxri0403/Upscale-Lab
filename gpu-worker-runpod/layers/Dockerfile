FROM pytorch/pytorch:2.5.1-cuda12.4-cudnn9-runtime
WORKDIR /app
ENV PYTHONUNBUFFERED=1 HF_HUB_OFFLINE=1 TRANSFORMERS_OFFLINE=1 PYTHONPATH=/app:/opt/lama
RUN apt-get update && apt-get install -y --no-install-recommends libglib2.0-0 libgl1 && rm -rf /var/lib/apt/lists/*
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
# Prepare reviewed, immutable model snapshots with prepare_models.py before building.
# No model code or weights are downloaded during a user job.
COPY models/birefnet /app/models/birefnet
COPY models/big-lama /app/models/big-lama
ARG LAMA_CHECKPOINT_SHA256
RUN test ${#LAMA_CHECKPOINT_SHA256} -eq 64 && \
    echo "$LAMA_CHECKPOINT_SHA256  /app/models/big-lama/models/best.ckpt" | sha256sum --check -
COPY models/lama-source /opt/lama
COPY models/versions.json /app/models/versions.json
COPY pipeline.py handler.py ./
RUN python -c "from saicinpainting.training.modules import make_generator; from transformers import AutoModelForImageSegmentation; from pipeline import generator_options; c = generator_options('/app/models/big-lama/config.yaml'); assert c['resnet_conv_kwargs']['ratio_gout'] == 0.75; assert c['downsample_conv_kwargs']['ratio_gin'] == 0; import handler; print('Worker imports and model config verified (no inference)')"
COPY licenses /app/licenses
CMD ["python", "-u", "handler.py"]
