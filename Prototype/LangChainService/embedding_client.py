from typing import List
import os
import requests
from dotenv import load_dotenv
from langchain_core.embeddings import Embeddings

load_dotenv()

# Default to the Docker service name so the container works without an explicit
# EMBEDDING_URL env var.  For local (non-Docker) development, set EMBEDDING_URL
# in your shell or a local .env file: EMBEDDING_URL=http://localhost:8001/embed
EMBEDDING_URL = os.getenv("EMBEDDING_URL", "http://embeddingservice:8001/embed")


class ExternalEmbeddingService(Embeddings):
    def embed_documents(self, texts: List[str]) -> List[List[float]]:
        vectors: List[List[float]] = []
        total = len(texts)

        for index, text in enumerate(texts, start=1):
            print(f"Embedding chunk {index}/{total}")

            response = requests.post(
                EMBEDDING_URL,
                json={"text": text},
                timeout=300
            )
            response.raise_for_status()
            data = response.json()

            if "embedding" not in data:
                raise ValueError(f"Embedding response missing 'embedding': {data}")

            vectors.append(data["embedding"])

        return vectors

    def embed_query(self, text: str) -> List[float]:
        response = requests.post(
            EMBEDDING_URL,
            json={"text": text},
            timeout=300
        )
        response.raise_for_status()
        data = response.json()

        if "embedding" not in data:
            raise ValueError(f"Embedding response missing 'embedding': {data}")

        return data["embedding"]