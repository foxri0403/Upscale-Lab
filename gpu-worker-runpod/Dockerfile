FROM pytorch/pytorch:2.4.1-cuda12.1-cudnn9-runtime

WORKDIR /app

ENV PYTHONUNBUFFERED=1
ENV PYTHONPATH=/app

RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        libglib2.0-0 \
        libgl1 && \
    rm -rf /var/lib/apt/lists/*

COPY requirements.txt .

RUN pip install --no-cache-dir \
    --no-deps \
    numpy==1.26.4 \
    opencv-python-headless==4.10.0.84 \
    Pillow==10.4.0

RUN pip install --no-cache-dir -r requirements.txt

COPY handler.py /app/handler.py

COPY models/RealESRGAN_x4plus.pth \
    /app/models/RealESRGAN_x4plus.pth

CMD ["python", "-u", "handler.py"]