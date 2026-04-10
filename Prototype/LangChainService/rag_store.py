import os
from functools import lru_cache

from langchain_community.vectorstores import FAISS
from embedding_client import ExternalEmbeddingService

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
INDEX_DIR = os.path.join(BASE_DIR, "data", "faiss_index")


@lru_cache(maxsize=1)
def get_vector_store():
    if not os.path.exists(INDEX_DIR):
        raise FileNotFoundError(f"{INDEX_DIR} not found. Run ingest.py first.")

    embeddings = ExternalEmbeddingService()

    return FAISS.load_local(
        INDEX_DIR,
        embeddings,
        allow_dangerous_deserialization=True
    )


def detect_query_topic(query: str) -> str:
    q = (query or "").lower()

    if "blue badge" in q:
        return "blue_badge"

    if "free school meal" in q or "free school meals" in q:
        return "free_school_meals"

    if "housing benefit" in q:
        return "housing_benefit"

    if "council tax reduction" in q:
        return "council_tax_reduction"

    if "council tax" in q:
        return "council_tax"

    if (
        "new bin" in q
        or "replacement bin" in q
        or "replacement container" in q
        or "new recycling bin" in q
        or "new recycle bin" in q
        or "bin cost" in q
        or "how much does the new bin cost" in q
        or "recycling container" in q
    ):
        return "new_bin"

    if "bin collection" in q or "bin day" in q or "collection day" in q:
        return "bin_collection"

    if "school admission" in q or "school place" in q:
        return "school_admissions"

    if "library" in q:
        return "libraries"

    if "planning" in q:
        return "planning"

    if "housing" in q or "homeless" in q:
        return "housing"

    return ""


def score_document(doc, query: str, service_hint: str = "", topic_hint: str = "") -> int:
    score = 0

    page_text = (doc.page_content or "").lower()
    title = (doc.metadata.get("title", "") or "").lower()
    url = (doc.metadata.get("url", "") or "").lower()
    service = (doc.metadata.get("service", "") or "").lower()
    topic = (doc.metadata.get("topic", "") or "").lower()

    query_lower = (query or "").lower()
    service_hint = (service_hint or "").lower()
    topic_hint = (topic_hint or "").lower()

    if service_hint and service == service_hint:
        score += 20

    if topic_hint and topic == topic_hint:
        score += 35

    if topic_hint and topic_hint in url:
        score += 10

    if "blue badge" in query_lower:
        if "blue badge" in title or "blue badge" in page_text:
            score += 40

    if "eligible" in query_lower or "eligibility" in query_lower or "qualify" in query_lower:
        if "who is it for" in page_text or "how do i qualify" in page_text or "qualify" in page_text or "eligible" in page_text:
            score += 20

    if "apply" in query_lower:
        if "how do i apply" in page_text or "apply" in title or "apply" in page_text:
            score += 20

    if "evidence" in query_lower or "proof" in query_lower:
        if "proof you need to provide" in title or "evidence" in page_text or "proof" in page_text:
            score += 25

    if "new bin" in query_lower or "replacement bin" in query_lower or "bin cost" in query_lower:
        if (
            "get-new-wheeled-bins-or-recycling-containers" in url
            or "replacement bin" in page_text
            or "replacement container" in page_text
            or "new wheeled bins" in page_text
            or "recycling containers" in page_text
        ):
            score += 40

    if "bin collection" in query_lower or "bin day" in query_lower:
        if "collection" in title or "collection" in page_text:
            score += 20

    bad_signals = [
        "privacy notice",
        "cookies",
        "accessibility statement",
        "a to z",
        "site navigation",
        "bank holiday closure",
        "adult entertainment venues",
        "scrap metal dealers licence",
    ]
    if any(x in title or x in url for x in bad_signals):
        score -= 100

    return score


def search_rag(query: str, service_hint: str = "", k: int = 6):
    db = get_vector_store()

    # Retrieve more candidates first, then rerank
    docs = db.similarity_search(query, k=max(k * 3, 12))

    topic_hint = detect_query_topic(query)

    scored_docs = []
    for doc in docs:
        score = score_document(doc, query, service_hint=service_hint, topic_hint=topic_hint)
        scored_docs.append((score, doc))

    scored_docs.sort(key=lambda x: x[0], reverse=True)

    top_docs = [doc for score, doc in scored_docs[:k]]

    return [
        {
            "text": d.page_content,
            "title": d.metadata.get("title", ""),
            "url": d.metadata.get("url", ""),
            "service": d.metadata.get("service", "Unknown"),
            "topic": d.metadata.get("topic", ""),
            "seed_group": d.metadata.get("seed_group", ""),
        }
        for d in top_docs
    ]