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

    # missed_bin: phrase list covers natural speech like "my bin was not collected"
    # as well as the original exact phrases.  Stays before bin_collection so the
    # specific reporting intent wins over the collection-schedule intent.
    if any(p in q for p in (
        "missed bin",
        "bin not collected",
        "missed collection",
        "bin was not collected",
        "bin wasn't collected",
        "bin has not been collected",
        "bins have not been collected",
        "failed to collect my bin",
        "failed to collect the bin",
        "not collected my bin",
    )):
        return "missed_bin"

    if "garden waste" in q:
        return "garden_waste"

    if "bin collection" in q or "bin day" in q or "collection day" in q:
        return "bin_collection"

    # school appeal — "place" added to match tools.py detect_query_intent
    if "appeal" in q and any(x in q for x in ("school", "admission", "place")):
        return "school_admissions"

    # school admissions: broader phrase list catches application/deadline queries
    # that don't use the exact phrase "school admission" or "school place"
    if any(p in q for p in (
        "school admission",
        "school place",
        "school application",
        "apply for school",
        "school deadline",
        "year 7",
        "reception place",
    )):
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
    title     = (doc.metadata.get("title", "")        or "").lower()
    url       = (doc.metadata.get("url", "")          or "").lower()
    service   = (doc.metadata.get("service", "")      or "").lower()
    topic     = (doc.metadata.get("topic", "")        or "").lower()

    # Rich metadata added by ingest.py — default to "" when absent (old index)
    heading      = (doc.metadata.get("heading", "")      or "").lower()
    section_hint = (doc.metadata.get("section_hint", "") or "").lower()
    chunk_type   = (doc.metadata.get("chunk_type", "")   or "").lower()

    query_lower  = (query or "").lower()
    service_hint = (service_hint or "").lower()
    topic_hint   = (topic_hint or "").lower()

    # ── Service / topic matching (unchanged) ─────────────────────────────────
    if service_hint and service == service_hint:
        score += 20

    if topic_hint and topic == topic_hint:
        score += 35

    if topic_hint and topic_hint in url:
        score += 10

    # ── FAQ chunk boost for question-style queries ────────────────────────────
    # FAQ chunks contain tightly paired Q+A content; they answer questions more
    # directly than standard prose chunks drawn from the same page.
    _QUESTION_STARTERS = (
        "what ", "how ", "when ", "where ", "why ",
        "can ", "do ", "does ", "is ", "am ", "will ", "should ",
    )
    is_question = query_lower.endswith("?") or any(
        query_lower.startswith(s) for s in _QUESTION_STARTERS
    )
    if is_question and chunk_type == "faq":
        score += 20

    # ── Heading / section_hint keyword boosts ────────────────────────────────
    # A heading match is a stronger signal than the same term appearing anywhere
    # in the body, because the heading labels the section's specific purpose.
    # We only award the boost when both the query AND the heading share the term.
    heading_field = f"{heading} {section_hint}"

    _HEADING_INTENT_BOOSTS: list[tuple[list[str], int]] = [
        (["apply", "application", "how to apply"],           15),
        (["eligible", "eligibility", "qualify", "who can"],  15),
        (["evidence", "proof", "documents needed"],          15),
        (["appeal", "challenge", "dispute"],                 20),
        (["cost", "price", "how much", "fee", "charge"],     15),
        (["arrears", "behind on", "struggling to pay"],      20),
        (["payment plan", "arrangement", "catch up"],        20),
        (["missed bin", "not collected", "missed collection"],20),
        (["replacement bin", "new bin", "new wheeled"],      15),
        (["deadline", "closing date", "last date"],          15),
    ]
    for terms, boost in _HEADING_INTENT_BOOSTS:
        if any(t in query_lower for t in terms) and any(t in heading_field for t in terms):
            score += boost

    # ── Existing targeted query / content boosts (unchanged) ─────────────────
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

    # ── New targeted intent boosts ────────────────────────────────────────────

    # School appeals — appeal query with a school/admission term anywhere in it
    if "appeal" in query_lower and any(t in query_lower for t in ("school", "admission", "place")):
        if "appeal" in heading_field or "appeal" in page_text:
            score += 30
        # Extra confirmation when the chunk is confirmed education content
        if "education" in service or topic == "school_admissions":
            score += 15

    # Council tax arrears / payment arrangement
    if "arrears" in query_lower or "behind" in query_lower or "struggling to pay" in query_lower:
        if "arrears" in page_text or "payment arrangement" in page_text or "payment plan" in page_text:
            score += 30
        if "council_tax" in topic:
            score += 10

    # Benefits eligibility — only when both "eligible" AND a benefit term appear
    if ("eligible" in query_lower or "eligibility" in query_lower) and any(
        t in query_lower for t in ("benefit", "universal credit", "pip", "housing benefit")
    ):
        if any(t in heading_field for t in ("eligible", "eligibility", "who can", "qualify")):
            score += 25

    # Replacement / new bin cost — only when cost intent meets bin intent
    if any(t in query_lower for t in ("cost", "how much", "price")) and any(
        t in query_lower for t in ("bin", "container")
    ):
        if any(t in heading_field for t in ("cost", "charge", "price", "fee")):
            score += 25
        elif "cost" in page_text or "charge" in page_text:
            score += 10

    # Missed bin — topic match on top of existing body-text checks
    if "missed bin" in query_lower or "bin not collected" in query_lower:
        if topic == "missed_bin":
            score += 30
        elif "missed bin" in heading_field or "missed bin" in page_text:
            score += 15

    # Payment plan / catch-up arrangement
    if any(t in query_lower for t in ("payment plan", "payment arrangement", "catch up")):
        if any(t in heading_field for t in ("payment plan", "arrangement", "catch up")):
            score += 30

    # URL-level boost: Bradford "problems-paying" family covers arrears AND arrangements
    if "council tax" in query_lower and any(t in query_lower for t in (
        "arrears", "behind", "struggling", "payment plan", "payment arrangement", "catch up",
    )):
        if "problems-paying" in url or "behind-on-your-council-tax" in url:
            score += 25

    # URL-level boost: "proof-you-need-to-provide" is the definitive Bradford evidence page
    if any(t in query_lower for t in ("evidence", "proof", "documents")):
        if "proof-you-need-to-provide" in url:
            score += 25

    # ── Penalties ────────────────────────────────────────────────────────────

    # Broad/general chunks are less useful when a specific topic is already known
    if topic_hint and topic == "general":
        score -= 10

    # Missed-bin queries want the reporting page, NOT the collection schedule
    if topic_hint == "missed_bin" and topic == "bin_collection":
        score -= 25

    # ── Bad-signal penalty (unchanged) ───────────────────────────────────────
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
            "text":         d.page_content,
            "title":        d.metadata.get("title", ""),
            "url":          d.metadata.get("url", ""),
            "service":      d.metadata.get("service", "Unknown"),
            "topic":        d.metadata.get("topic", ""),
            "seed_group":   d.metadata.get("seed_group", ""),
            # Richer fields from ingest.py — empty string when absent (old index)
            "heading":      d.metadata.get("heading", ""),
            "section_hint": d.metadata.get("section_hint", ""),
            "chunk_type":   d.metadata.get("chunk_type", ""),
        }
        for d in top_docs
    ]