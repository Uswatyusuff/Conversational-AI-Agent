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

    


def rag_search_tool(query: str, service_hint: str = ""):
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
        answer = generate_answer_with_llm(query, "\n\n".join(context_chunks))
        answer = post_process_answer(answer, query)

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

    answer = generate_answer_with_llm(query, context)
    answer = post_process_answer(answer, query)

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
        elif intent == "benefits_general":
            score += 20 if service == "benefits & support" else 0

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

    return score


def build_context_chunks(results, max_chunks: int = 3, max_chars_per_chunk: int = 1400):
    chunks = []
    seen = set()

    for result in results[:max_chunks]:
        raw_text = result.get("text", "")
        cleaned = strip_metadata_headers(raw_text)

        if not cleaned:
            continue

        cleaned = cleaned[:max_chars_per_chunk].strip()

        if cleaned in seen:
            continue

        seen.add(cleaned)
        chunks.append(cleaned)

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
        filtered_lines.append(line)

    return " ".join(filtered_lines).strip()


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

    return results[0].get("url", "")


def generate_answer_with_llm(query, context):
    client = get_openai_client()

    prompt = f"""
You are a Bradford Council assistant.

Answer the user's question using ONLY the provided context.

STRICT RULES:
- Give a direct, specific answer
- Do not be vague
- If the user asks how to apply, explain the application steps briefly
- If the user asks about eligibility, answer eligibility directly
- If the user asks about evidence, answer evidence directly
- Do not answer a different service by mistake
- Only include prices, dates, phone numbers, email addresses, and rules if they are explicitly present in the context
- If the context is unclear or conflicting, say that briefly and refer the user to the official page in next steps
- Do not invent prices, deadlines, phone numbers, email addresses, or rules
- Do not say "based on the context provided"
- Keep the answer concise, usually 2 to 4 sentences
- If the context does not clearly contain the answer, say that briefly
User question:
{query}

Context:
{context}

Return only the final answer.
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