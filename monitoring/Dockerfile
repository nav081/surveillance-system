FROM python:3.11-slim AS base

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1

WORKDIR /app

# Build tools are needed for insightface's Cython extensions. They stay in the image for simplicity.
RUN apt-get update \
    && apt-get install -y --no-install-recommends build-essential libglib2.0-0 \
    && rm -rf /var/lib/apt/lists/*

COPY requirements.txt .
# ultralytics pulls in the GUI build of opencv, which needs libxcb. Replace it with the headless build the engine pins.
RUN pip install -r requirements.txt \
    && pip uninstall -y opencv-python \
    && pip install --force-reinstall --no-deps "opencv-python-headless>=4.10,<4.12"

COPY engine/ engine/
COPY enroll.py .

# Non-root runtime user. Model weights are mounted at /models; InsightFace packs cache under /home/engine/.insightface.
RUN useradd --create-home --uid 10001 engine \
    && mkdir -p /models /home/engine/.insightface \
    && chown -R engine /app /models /home/engine/.insightface
USER engine

CMD ["python", "-m", "engine"]
