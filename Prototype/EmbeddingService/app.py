from fastapi import FastAPI
from pydantic import BaseModel
from sentence_transformers import SentenceTransformer
import numpy as np

app = FastAPI(title="Free Semantic Embedding Service")

# Small + fast + strong baseline embedding model
# Perfect for RAG-style FAQ retrieval
MODEL_NAME = "sentence-transformers/all-MiniLM-L6-v2"
model = SentenceTransformer(MODEL_NAME)

class EmbedRequest(BaseModel):
    text: str

class EmbedBatchRequest(BaseModel):
    texts: list[str]

@app.get("/health")
def health():
    return {"status": "ok", "model": MODEL_NAME}

@app.post("/embed")
def embed(req: EmbedRequest):
    vec = model.encode(req.text, normalize_embeddings=True)
    return {"embedding": vec.tolist(), "dim": len(vec)}

@app.post("/embed-batch")
def embed_batch(req: EmbedBatchRequest):
    vecs = model.encode(req.texts, normalize_embeddings=True)
    return {
        "embeddings": [v.tolist() for v in vecs],
        "dim": int(vecs.shape[1]),
        "count": int(vecs.shape[0])
    }