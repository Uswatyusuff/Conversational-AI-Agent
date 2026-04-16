FROM python:3.11-slim

WORKDIR /app

# Install curl for the HEALTHCHECK instruction below.
# Using --no-install-recommends keeps the layer small.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY requirements.txt .

# Install PyTorch CPU-only first (avoids pulling in the 2 GB CUDA variant),
# then the rest of the requirements.
RUN pip install --no-cache-dir --upgrade pip \
    && pip install --no-cache-dir torch --index-url https://download.pytorch.org/whl/cpu \
    && pip install --no-cache-dir -r requirements.txt

COPY . .

EXPOSE 8001

# Verify the service is up and the model is loaded before marking healthy.
# start_period gives the model time to load on cold start (can take 30-60 s).
HEALTHCHECK --interval=30s --timeout=10s --start-period=90s --retries=5 \
    CMD curl -f http://localhost:8001/health || exit 1

CMD ["uvicorn", "app:app", "--host", "0.0.0.0", "--port", "8001"]
