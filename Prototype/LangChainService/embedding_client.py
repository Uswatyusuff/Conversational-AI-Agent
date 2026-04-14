from typing import List
import os
import requests
from dotenv import load_dotenv
from langchain_core.embeddings import Embeddings

load_dotenv()

EMBEDDING_URL = os.getenv("EMBEDDING_URL", "http://127.0.0.1:8001/embed")


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