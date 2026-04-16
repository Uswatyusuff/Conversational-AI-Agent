import os
import re
import requests
from dotenv import load_dotenv
from openai import OpenAI

from rag_store import search_rag

load_dotenv()

DOTNET_BASE_URL = os.getenv("DOTNET_BASE_URL", "http://localhost:8080")


def looks_like_postcode(text: str) -> bool:
    pattern = r"\b[A-Z]{1,2}\d[A-Z\d]?\s?\d[A-Z]{2}\b"
    return re.search(pattern, text.upper()) is not None


def extract_postcode(text: str) -> str:
    pattern = r"\b[A-Z]{1,2}\d[A-Z\d]?\s?\d[A-Z]{2}\b"
    match = re.search(pattern, text.upper())
    return match.group(0).strip() if match else ""


def get_openai_client():
    api_key = os.getenv("OPENAI_API_KEY")
    if not api_key:
        raise ValueError("OPENAI_API_KEY is not set")
    return OpenAI(api_key=api_key)


def postcode_lookup_tool(postcode: str):
    response = requests.get(
        f"{DOTNET_BASE_URL}/api/postcode/search",
        params={"postcode": postcode},
        timeout=60
    )
    response.raise_for_status()
    return response.json()


def bin_result_tool(postcode: str, address: str):
    response = requests.get(
        f"{DOTNET_BASE_URL}/api/postcode/bin-result",
        params={"postcode": postcode, "address": address},
        timeout=90
    )
    response.raise_for_status()
    return response.json()


def detect_query_intent(query: str) -> str:
    q = (query or "").lower().strip()

    if "benefit" in q or "benefits" in q:
        # evidence before eligibility: "what proof do I need for benefits?" has both terms
        if any(x in q for x in ["evidence", "proof", "documents", "what do i need"]):
            return "benefits_evidence"

        if any(x in q for x in ["can i get", "am i eligible", "eligible", "qualify", "can i apply"]):
            return "benefits_eligibility"

        if any(x in q for x in ["how can i get", "how do i get", "how do i apply", "apply"]):
            return "benefits_apply"

        if any(x in q for x in ["how much", "amount", "how much can i get", "how much benefits"]):
            return "benefits_amount"

        return "benefits_general"

    if "blue badge" in q and any(x in q for x in ["apply", "application", "how do i apply"]):
        return "blue_badge_apply"

    if "blue badge" in q and any(x in q for x in ["eligible", "eligibility", "qualify", "am i eligible"]):
        return "blue_badge_eligibility"

    if "blue badge" in q and any(x in q for x in ["evidence", "proof", "documents"]):
        return "blue_badge_evidence"

    if "free school meal" in q and "apply" in q:
        return "free_school_meals_apply"

    if "free school meal" in q and any(x in q for x in ["eligible", "eligibility", "qualify"]):
        return "free_school_meals_eligibility"

    # school_appeal before generic school/admission checks
    if "appeal" in q and any(x in q for x in ["school", "admission", "place"]):
        return "school_appeal"

    # council_tax sub-intents before the generic balance/reduction checks
    if "council tax" in q and any(x in q for x in [
        "arrears", "behind", "struggling to pay", "can't pay", "cannot pay",
        "debt", "overdue", "enforcement",
    ]):
        return "council_tax_arrears"

    if "council tax" in q and any(x in q for x in [
        "payment plan", "payment arrangement", "catch up", "spread payments",
        "repayment plan", "repay",
    ]):
        return "council_tax_payment_arrangement"

    if "council tax" in q and any(x in q for x in ["balance", "check balance"]):
        return "council_tax_balance"

    if "council tax" in q and any(x in q for x in ["discount", "reduction"]):
        return "council_tax_reduction"

    if any(x in q for x in [
        "new bin",
        "replacement bin",
        "new recycle bin",
        "new recycling bin",
        "recycling container",
        "replacement container",
    ]):
        if any(x in q for x in ["cost", "price", "charge", "fee", "how much"]):
            return "new_bin_cost"
        return "new_bin_request"

    # missed_bin before bin_collection: "my bin wasn't collected" ≠ asking for the schedule
    if any(x in q for x in [
        "missed bin", "bin not collected", "bin wasn't collected",
        "bin was not collected", "missed collection", "didn't collect my bin",
        "not been collected", "failed to collect",
    ]):
        return "missed_bin"

    if any(x in q for x in ["bin collection", "bin day", "collection day", "next collection"]):
        return "bin_collection"

    if "planning" in q or "planning application" in q:
        return "planning"

    if any(x in q for x in ["library", "libraries", "renew books", "renew library books", "e-books", "digital library"]):
        return "libraries"

    
    if any(x in q for x in ["evidence", "proof", "documents"]):
        return "evidence"
    
    if any(x in q for x in [
    "housing",
    "homeless",
    "homelessness",
    "housing support",
    "rent help",
    "rent support",
    "eviction",
    "no place to stay"
]):
        return "housing"

    if "apply" in q:
        return "apply"

    if any(x in q for x in ["eligible", "eligibility", "qualify"]):
        return "eligibility"

    return ""

    


def is_weak_answer(answer: str) -> bool:
    """
    Return True when the LLM's answer is too vague, hedges instead of answering,
    or explicitly says the context is missing/unclear.  Callers should treat a
    weak answer as empty so the outer clarification logic can take over.
    """
    if not answer or len(answer.strip()) < 20:
        return True

    lower = answer.lower()

    _WEAK_PHRASES = [
        # Explicit context-gap phrases (from the LLM prompt instructions)
        "the context does not clearly",
        "context does not clearly",
        "not specified in the context",
        "not clearly contain the answer",
        "not clearly contain",
        "unclear or conflicting",
        "context does not provide",
        "context does not contain",
        "the provided context does not",
        "not available in the provided context",
        "not mentioned in the context",
        "not explicitly stated",
        "no specific information",
        # Referral deflections — "please refer to the official page"
        "please refer to the official",
        "refer to the official page",
        "refer to the official bradford",
        "please visit the official",
        "please check the official",
        # "I can't / I'm not able to" hedges
        "i'm not able to",
        "i am not able to",
        "not able to find",
        "i cannot find",
        "i can't find",
        "i could not find",
        "i don't have specific information",
        "i do not have specific information",
        "based on the context provided, i cannot",
        # Vague recommendation deflections
        "i would recommend checking",
        "i would suggest visiting",
        "i would advise visiting",
        "not clearly specified",
    ]

    return any(phrase in lower for phrase in _WEAK_PHRASES)


def rag_search_tool(query: str, service_hint: str = "", history: list = None):
    results = search_rag(query, service_hint, 12)

    if not results:
        return {
            "answer": "",
            "service": service_hint or "Unknown",
            "nextStepsUrl": ""
        }

    query_lower = (query or "").lower()
    query_lower = query_lower.replace("recycle bin", "recycling bin")
    query_lower = query_lower.replace("new recycle bin", "new recycling bin")

    intent = detect_query_intent(query_lower)

    exact_match = find_exact_intent_match(query_lower, results, intent)
    if exact_match is not None:
        context_chunks = build_context_chunks([exact_match], max_chunks=1, max_chars_per_chunk=1800)
        answer = generate_answer_with_llm(query, "\n\n".join(context_chunks), history=history)
        answer = post_process_answer(answer, query)

        # If the LLM hedged or said the context is missing, return empty so the
        # outer clarification logic can ask a targeted follow-up instead.
        if is_weak_answer(answer):
            answer = ""

        return {
            "answer": answer,
            "service": service_hint or exact_match.get("service") or "Unknown",
            "nextStepsUrl": exact_match.get("url", "")
        }

    scored = []
    for result in results:
        text = (result.get("text") or "").lower()
        title = (result.get("title") or "").lower()
        url = (result.get("url") or "").lower()
        service = (result.get("service") or "").lower()
        topic = (result.get("topic") or "").lower()
        seed_group = (result.get("seed_group") or "").lower()

        score = score_result(
            query_lower=query_lower,
            text=text,
            title=title,
            url=url,
            service=service,
            topic=topic,
            seed_group=seed_group,
            intent=intent,
            service_hint=(service_hint or "").lower()
        )
        scored.append((score, result))

    scored.sort(key=lambda x: x[0], reverse=True)
    ranked_results = [item[1] for item in scored]
    best = ranked_results[0]

    top_chunks = build_context_chunks(ranked_results, max_chunks=2, max_chars_per_chunk=1400)
    context = "\n\n".join(top_chunks)

    answer = generate_answer_with_llm(query, context, history=history)
    answer = post_process_answer(answer, query)

    # If the LLM hedged or said the context is missing, return empty so the
    # outer clarification logic can ask a targeted follow-up instead.
    if is_weak_answer(answer):
        answer = ""

    return {
        "answer": answer,
        "service": service_hint or best.get("service") or "Unknown",
        "nextStepsUrl": choose_best_url(query_lower, ranked_results, intent)
    }


def find_exact_intent_match(query_lower: str, results, intent: str):
    if intent in {"blue_badge_apply", "blue_badge_eligibility", "blue_badge_evidence"}:
        for result in results:
            url = (result.get("url") or "").lower()
            title = (result.get("title") or "").lower()
            if "blue-badge-scheme" in url or "blue badge scheme" in title:
                return result

    if intent in {"free_school_meals_apply", "free_school_meals_eligibility"}:
        for result in results:
            url = (result.get("url") or "").lower()
            if "free-school-meals" in url:
                return result

    if intent == "council_tax_balance":
        for result in results:
            url = (result.get("url") or "").lower()
            if "myinfo" in url or "pay-your-council-tax" in url:
                return result

    if intent in {"new_bin_cost", "new_bin_request"}:
        preferred_patterns = [
            "get-new-wheeled-bins-or-recycling-containers",
            "wheeled-bins-and-recycling-containers",
            "replacement-bins",
            "recycling-containers",
        ]
        for pattern in preferred_patterns:
            for result in results:
                url = (result.get("url") or "").lower()
                title = (result.get("title") or "").lower()
                text = (result.get("text") or "").lower()
                if pattern in url or pattern in title or pattern in text:
                    return result

    if intent in {"council_tax_arrears", "council_tax_payment_arrangement"}:
        for result in results:
            url = (result.get("url") or "").lower()
            if "problems-paying" in url or "behind-on-your-council-tax" in url or "arrears" in url:
                return result

    if intent == "missed_bin":
        for result in results:
            url = (result.get("url") or "").lower()
            if "missed-bin" in url or "report-missed" in url or "missed-collection" in url:
                return result

    if intent == "school_appeal":
        # prefer a page whose URL contains both "school"/"admission" and "appeal"
        for result in results:
            url = (result.get("url") or "").lower()
            if "appeal" in url and ("school" in url or "admission" in url):
                return result
        # fall back to any page that discusses appeals in body text
        for result in results:
            text = (result.get("text") or "").lower()
            title = (result.get("title") or "").lower()
            if "appeal" in text and ("school" in text or "admission" in title):
                return result

    if intent == "benefits_evidence":
        for result in results:
            url = (result.get("url") or "").lower()
            title = (result.get("title") or "").lower()
            if "proof-you-need-to-provide" in url or "evidence" in url:
                return result
        for result in results:
            text = (result.get("text") or "").lower()
            if "proof you need to provide" in text or "evidence you need" in text:
                return result

    return None


def score_result(
    query_lower: str,
    text: str,
    title: str,
    url: str,
    service: str,
    topic: str,
    seed_group: str,
    intent: str,
    service_hint: str = ""
) -> int:
    blob = f"{title} {url} {text}"
    score = 0

    if service_hint and service == service_hint:
        score += 20

    if topic:
        if intent == "blue_badge_apply" and topic == "blue_badge":
            score += 45
        elif intent == "blue_badge_eligibility" and topic == "blue_badge":
            score += 45
        elif intent == "blue_badge_evidence" and topic == "blue_badge":
            score += 45
        elif intent == "free_school_meals_apply" and topic == "free_school_meals":
            score += 45
        elif intent == "free_school_meals_eligibility" and topic == "free_school_meals":
            score += 45
        elif intent == "council_tax_balance" and topic == "council_tax":
            score += 35
        elif intent == "new_bin_cost" and topic == "new_bin":
            score += 50
        elif intent == "new_bin_request" and topic == "new_bin":
            score += 50
        elif intent == "bin_collection" and topic == "bin_collection":
            score += 50

        elif intent == "planning" and topic == "planning":
            score += 45
        elif intent == "libraries" and topic == "libraries":
            score += 45
        elif intent == "housing" and topic == "housing":
            score += 45

        elif intent == "benefits_eligibility" and topic in {"housing_benefit", "general"}:
            score += 35
        elif intent == "benefits_apply" and topic in {"housing_benefit", "free_school_meals", "general"}:
            score += 35
        elif intent == "benefits_amount" and topic in {"housing_benefit", "general"}:
            score += 30
        elif intent == "benefits_evidence" and topic in {
            "housing_benefit", "free_school_meals", "blue_badge", "council_tax_reduction", "general"
        }:
            score += 40
        elif intent == "benefits_general":
            score += 20 if service == "benefits & support" else 0

        elif intent == "council_tax_arrears" and topic == "council_tax":
            score += 45
        elif intent == "council_tax_payment_arrangement" and topic == "council_tax":
            score += 45

        elif intent == "missed_bin" and topic == "missed_bin":
            score += 55

        elif intent == "school_appeal" and topic == "school_admissions":
            score += 45

    if "blue badge" in query_lower:
        if "blue badge" in blob:
            score += 35
        if "blue-badge" in url:
            score += 50

    if "free school meals" in query_lower:
        if "free school meals" in blob:
            score += 35
        if "free-school-meals" in url:
            score += 40

    if "council tax" in query_lower and "council tax" in blob:
        score += 20

    if any(x in query_lower for x in ["apply", "application"]):
        if "apply" in blob:
            score += 12

    if any(x in query_lower for x in ["eligible", "eligibility", "qualify"]):
        if any(x in blob for x in ["eligible", "eligibility", "qualify", "who is it for", "how do i qualify"]):
            score += 18

    if any(x in query_lower for x in ["evidence", "proof", "documents"]):
        if any(x in blob for x in ["proof", "evidence", "documents", "information you need to provide"]):
            score += 20

    if intent in {"new_bin_cost", "new_bin_request"}:
        if "get-new-wheeled-bins-or-recycling-containers" in url:
            score += 60
        if "wheeled-bins-and-recycling-containers" in url:
            score += 40
        if "replacement-bins" in url or "recycling-containers" in url:
            score += 30
        if any(x in blob for x in ["new wheeled bins", "recycling containers", "replacement container", "replacement bin"]):
            score += 25

    if intent == "bin_collection":
        if "check-your-bin-collection-dates" in url:
            score += 50
        if "collection" in blob:
            score += 15

    if "myinfo" in url and "council tax" in query_lower:
        score += 20

    if "waste & bins" in service and any(x in query_lower for x in ["bin", "bins", "waste", "recycling"]):
        score += 10

    if "benefits & support" in service and "blue badge" in query_lower:
        score += 10

    bad_signals = [
        "privacy notice",
        "cookies",
        "accessibility statement",
        "a to z",
        "site navigation",
        "bank holiday closure",
        "adult entertainment venues",
        "scrap metal dealers licence",
        "club premises certificate",
        "petitions",
    ]
    if any(x in blob for x in bad_signals):
        score -= 100

    if "check-your-bin-collection-dates" in url and intent in {"new_bin_cost", "new_bin_request"}:
        score -= 60

    if "garden-waste-bin" in url and intent in {"new_bin_cost", "new_bin_request"} and "garden" not in query_lower:
        score -= 50

    if "household waste recycling centre" in blob and intent in {"new_bin_cost", "new_bin_request"}:
        score -= 40

    if "club premises certificate" in blob and "blue badge" in query_lower:
        score -= 100

    if "petition" in blob and "apply" in query_lower and "blue badge" in query_lower:
        score -= 100

    if "planning" in query_lower:
        if "planning" in blob:
            score += 20
        if "planning-application" in url or "planning-applications" in url:
            score += 30
        if "view planning applications" in blob:
            score += 25

    if any(x in query_lower for x in ["library", "libraries", "renew books", "renew library books", "e-books", "digital library"]):
        if "library" in blob or "libraries" in blob:
            score += 20
        if "renewing-borrowing-and-reserving-items" in url:
            score += 30
        if "e-books" in url or "digital-library" in url:
            score += 20

    if intent == "housing":
        if topic == "housing":
            score += 50
        if "housing" in blob or "homeless" in blob:
            score += 30
        if "housing" in url or "homelessness" in url:
            score += 25

    # prevent benefits pages hijacking housing queries
    if intent == "housing" and "benefits" in service:
        score -= 20

    if "benefit" in query_lower or "benefits" in query_lower:
        if "benefit" in blob or "benefits" in blob:
            score += 20
        if "benefits & support" in service:
            score += 25
        if "benefits" in url:
            score += 15

    if intent == "benefits_apply":
        if "apply" in blob or "application" in blob:
            score += 15

    if intent == "benefits_eligibility":
        if any(x in blob for x in ["qualify", "eligible", "entitled"]):
            score += 15

    if intent == "benefits_amount":
        if any(x in blob for x in ["how much", "entitled", "calculator", "reduction"]):
            score += 15

    if intent == "benefits_evidence":
        if any(x in blob for x in ["evidence", "proof", "documents", "information you need to provide", "what you need to provide"]):
            score += 30
        if "proof-you-need-to-provide" in url or "evidence" in url:
            score += 35

    if intent == "council_tax_arrears":
        if any(x in blob for x in ["arrears", "behind", "struggling to pay", "enforcement", "liability order"]):
            score += 30
        if "problems-paying" in url or "arrears" in url or "behind-on-your-council-tax" in url:
            score += 35
        # de-rank pages that are only about paying normally, not about debt
        if "pay-your-council-tax" in url and "arrears" not in blob:
            score -= 20

    if intent == "council_tax_payment_arrangement":
        if any(x in blob for x in ["payment plan", "payment arrangement", "catch up", "spread", "repayment"]):
            score += 30
        if "problems-paying" in url or "payment-plan" in url or "behind-on-your-council-tax" in url:
            score += 35

    if intent == "missed_bin":
        if any(x in blob for x in ["missed bin", "not collected", "report a missed", "failed to collect"]):
            score += 30
        if "missed-bin" in url or "report-missed" in url or "missed-collection" in url:
            score += 35
        # de-rank collection schedule pages for missed-bin queries
        if "check-your-bin-collection-dates" in url:
            score -= 30

    if intent == "school_appeal":
        if "appeal" in blob:
            score += 30
        if "appeal" in url:
            score += 35
        if "school-admissions" in url or "admissions" in url:
            score += 20

    return score


def build_context_chunks(results, max_chunks: int = 3, max_chars_per_chunk: int = 1400):
    """
    Assemble the best context blocks from a ranked result list.

    Improvements over the original:
    - Uses extract_chunk_parts() to preserve HEADING: as a section label.
    - Near-duplicate detection on normalised body fingerprint (first 200 chars)
      so overlap chunks from the same page don't pad the context.
    - Iterates the full ranked list until max_chunks non-duplicate chunks are
      found, rather than hard-slicing at [:max_chunks] before dedup.
    - Soft-truncates each body to a sentence boundary via _truncate_to_sentence().
    - Prefixes chunks with [Heading] when present so the LLM knows which section
      of the page it is reading.
    """
    chunks: list[str] = []
    seen_fps: set[str] = set()

    for result in results:
        if len(chunks) >= max_chunks:
            break

        raw_text = result.get("text", "")
        heading, body = extract_chunk_parts(raw_text)

        if not body:
            continue

        # Near-duplicate detection: normalise whitespace and fingerprint on first
        # 200 chars.  Catches chunks that differ only in truncation position or
        # minor whitespace variation from the same underlying section.
        fp = " ".join(body.lower().split())[:200]
        if fp in seen_fps:
            continue
        seen_fps.add(fp)

        # Soft-truncate body so it ends at a sentence boundary where possible
        body = _truncate_to_sentence(body, max_chars_per_chunk)

        # Prefix with section heading when available.  Capped at 80 chars to
        # guard against any very long headings from the heuristic detector.
        if heading:
            chunk_text = f"[{heading[:80]}]\n{body}"
        else:
            chunk_text = body

        chunks.append(chunk_text)

    return chunks


def strip_metadata_headers(raw_text: str) -> str:
    lines = [line.strip() for line in raw_text.splitlines() if line.strip()]
    filtered_lines = []

    for line in lines:
        if line.startswith("TITLE:"):
            continue
        if line.startswith("URL:"):
            continue
        if line.startswith("SERVICE:"):
            continue
        if line.startswith("TOPIC:"):
            continue
        if line.startswith("HEADING:"):
            continue
        filtered_lines.append(line)

    return " ".join(filtered_lines).strip()


def extract_chunk_parts(raw_text: str) -> tuple[str, str]:
    """
    Split an indexed-text block into (heading, body).
    - Drops TITLE / URL / SERVICE / TOPIC header lines (noise for the LLM).
    - Captures the HEADING: line as a user-friendly section label.
    - Joins remaining content lines as the body.
    Returns ("", body) when no HEADING: is present — degrades gracefully
    on chunks from an older index that pre-dates ingest.py heading support.
    """
    lines = [l.strip() for l in raw_text.splitlines() if l.strip()]
    heading = ""
    body_lines: list[str] = []

    for line in lines:
        if any(line.startswith(p) for p in ("TITLE:", "URL:", "SERVICE:", "TOPIC:")):
            continue
        if line.startswith("HEADING:"):
            heading = line[len("HEADING:"):].strip()
        else:
            body_lines.append(line)

    return heading, " ".join(body_lines).strip()


def _truncate_to_sentence(text: str, limit: int) -> str:
    """
    Truncate to at most `limit` chars, preferring a sentence boundary so the
    LLM receives complete thoughts rather than mid-sentence cuts.
    Falls back to a hard cut only when no boundary is found in the second half.
    """
    if len(text) <= limit:
        return text
    cut = text[:limit]
    # Scan backward for the last sentence-ending punctuation before the limit
    last_stop = max(cut.rfind(". "), cut.rfind(".\n"), cut.rfind("? "), cut.rfind("! "))
    if last_stop > limit // 2:
        return text[:last_stop + 1].strip()
    return cut.rstrip()


def post_process_answer(answer: str, query: str) -> str:
    if not answer:
        return ""

    query_lower = query.lower()
    answer_lower = answer.lower()

    if any(x in query_lower for x in ["cost", "price", "charge", "fee"]) and any(
        x in answer_lower for x in ["not specified", "not listed", "not mentioned", "not available in the provided context"]
    ):
        return (
            answer.rstrip() +
            " You can use the official council page in the next steps link to check the latest charge or request details."
        )

    return answer.strip()


def choose_best_url(query_lower: str, results, intent: str = "") -> str:
    if not results:
        return ""

    if intent in {"new_bin_cost", "new_bin_request"}:
        for pattern in [
            "get-new-wheeled-bins-or-recycling-containers",
            "wheeled-bins-and-recycling-containers",
            "replacement-bins",
            "recycling-containers",
        ]:
            for result in results:
                url = (result.get("url") or "").lower()
                if pattern in url:
                    return result.get("url", "")

    if intent in {"blue_badge_apply", "blue_badge_eligibility", "blue_badge_evidence"}:
        for result in results:
            url = (result.get("url") or "").lower()
            if "blue-badge-scheme" in url or "blue-badge" in url:
                return result.get("url", "")

    if intent in {"free_school_meals_apply", "free_school_meals_eligibility"}:
        for result in results:
            url = (result.get("url") or "").lower()
            if "free-school-meals" in url:
                return result.get("url", "")

    if intent == "council_tax_balance":
        for result in results:
            url = (result.get("url") or "").lower()
            if "myinfo" in url:
                return result.get("url", "")
        for result in results:
            url = (result.get("url") or "").lower()
            if "pay-your-council-tax" in url:
                return result.get("url", "")

    if intent == "bin_collection":
        for result in results:
            url = (result.get("url") or "").lower()
            if "check-your-bin-collection-dates" in url:
                return result.get("url", "")
            
    if intent == "planning":
        for result in results:
            url = (result.get("url") or "").lower()
            if "view-planning-applications" in url:
                return result.get("url", "")
        for result in results:
            url = (result.get("url") or "").lower()
            if "planning-applications" in url:
                return result.get("url", "")

    if intent == "libraries":
        for result in results:
            url = (result.get("url") or "").lower()
            if "renewing-borrowing-and-reserving-items" in url:
                return result.get("url", "")
        for result in results:
            url = (result.get("url") or "").lower()
            if "digital-library" in url or "e-books" in url:
                return result.get("url", "")

    if intent == "housing":
        for result in results:
            url = (result.get("url") or "").lower()
            if "homelessness" in url:
                return result.get("url", "")
        for result in results:
            url = (result.get("url") or "").lower()
            if "housing" in url:
                return result.get("url", "")
            
    if intent in {"benefits_apply", "benefits_eligibility", "benefits_general"}:
        for result in results:
            url = (result.get("url") or "").lower()
            if "benefits-and-welfare-advice-and-help" in url:
                return result.get("url", "")
        for result in results:
            url = (result.get("url") or "").lower()
            if "benefits-faqs" in url:
                return result.get("url", "")

    if intent == "benefits_amount":
        for result in results:
            url = (result.get("url") or "").lower()
            if "housing-benefit-and-council-tax-reduction" in url:
                return result.get("url", "")
        for result in results:
            url = (result.get("url") or "").lower()
            if "benefits-notification-explained" in url:
                return result.get("url", "")

    if intent == "benefits_evidence":
        for result in results:
            url = (result.get("url") or "").lower()
            if "proof-you-need-to-provide" in url or "evidence" in url:
                return result.get("url", "")
        for result in results:
            url = (result.get("url") or "").lower()
            if "benefits" in url:
                return result.get("url", "")

    if intent in {"council_tax_arrears", "council_tax_payment_arrangement"}:
        for result in results:
            url = (result.get("url") or "").lower()
            if "problems-paying" in url or "behind-on-your-council-tax" in url:
                return result.get("url", "")
        for result in results:
            url = (result.get("url") or "").lower()
            if "council-tax" in url:
                return result.get("url", "")

    if intent == "missed_bin":
        for result in results:
            url = (result.get("url") or "").lower()
            if "missed-bin" in url or "report-missed" in url or "missed-collection" in url:
                return result.get("url", "")
        for result in results:
            url = (result.get("url") or "").lower()
            if "bins" in url or "waste" in url:
                return result.get("url", "")

    if intent == "school_appeal":
        for result in results:
            url = (result.get("url") or "").lower()
            if "appeal" in url:
                return result.get("url", "")
        for result in results:
            url = (result.get("url") or "").lower()
            if "school-admissions" in url or "admissions" in url:
                return result.get("url", "")

    return results[0].get("url", "")


def generate_answer_with_llm(query, context, history: list = None):
    client = get_openai_client()

    # Build a conversation history block so the LLM can handle follow-up questions
    history_block = ""
    if history:
        lines = []
        for turn in history[-6:]:  # last 6 turns is plenty of context
            role = getattr(turn, "role", turn.get("role", "")) if not hasattr(turn, "role") else turn.role
            msg = getattr(turn, "message", turn.get("message", "")) if not hasattr(turn, "message") else turn.message
            if role and msg:
                label = "Resident" if role.lower() == "user" else "Assistant"
                lines.append(f"{label}: {msg}")
        if lines:
            history_block = "Recent conversation (for context — use this to understand follow-up questions):\n" + "\n".join(lines) + "\n\n"

    prompt = f"""
You are a Bradford Council assistant. Answer the resident's question directly and helpfully.

ANSWER STYLE:
- Lead with the answer, not a preamble
- Write as a council advisor speaking to a resident — plain, clear British English
- 2 to 4 sentences unless the question genuinely needs more
- Never say "based on the context", "the context says", or "according to the context"
- Never say "I'm not able to", "I cannot find", or "I don't have information"

CONTENT RULES:
- Use only facts that appear in the information below
- Do not invent prices, dates, deadlines, phone numbers, email addresses, or eligibility rules
- Only quote a specific figure or rule if it is explicitly stated

QUESTION-TYPE GUIDANCE:
- Eligibility / "who qualifies": state the qualifying conditions directly, e.g. "You may qualify if..."
- How to apply / application steps: give the steps or route briefly, e.g. "You can apply online at..." or "To apply, you will need to..."
- Evidence / documents needed: list what is needed plainly, e.g. "You will need to provide..."
- Appeals: explain the appeal process or grounds plainly
- Arrears / struggling to pay / payment arrangement: explain the options available, e.g. "If you are behind on payments, you can contact the council to set up a payment arrangement..."
- Missed bin / collection not done: tell the resident how to report it

FOLLOW-UP QUESTIONS:
- If the resident's question is a follow-up (e.g. "am I eligible if I have ADHD", "what about the cost?"), use the recent conversation above to understand what topic they are referring to and answer accordingly
- Never ask the resident to repeat information they have already given

FALLBACK (last resort only — use sparingly):
- If the information genuinely does not cover the question, say one short sentence: "I don't have the specific details for that — please check the official Bradford Council website or contact the council directly."
- Do not use the fallback when an approximate or partial answer can be given

{history_block}Resident's current question:
{query}

Information:
{context}

Answer:
"""

    response = client.chat.completions.create(
        model="gpt-4o-mini",
        messages=[
            {"role": "system", "content": "You are a concise and helpful UK council assistant."},
            {"role": "user", "content": prompt}
        ],
        temperature=0.1
    )

    return response.choices[0].message.content.strip()