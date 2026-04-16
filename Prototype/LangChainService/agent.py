from tools import (
    rag_search_tool,
    looks_like_postcode,
    extract_postcode
)


def normalize(text: str) -> str:
    if not text:
        return ""
    return " ".join(text.lower().strip().split())


def is_collection_date_intent(lower: str) -> bool:
    return (
        "bin collection" in lower or
        "collection day" in lower or
        "bin day" in lower or
        "next collection" in lower or
        "next bin collection" in lower or
        "recycling collection" in lower or
        "recycling collection day" in lower or
        "garden waste collection" in lower or
        ("when" in lower and "bin" in lower and "collection" in lower) or
        ("when" in lower and "recycling" in lower and "collection" in lower)
    )


def is_new_bin_intent(lower: str) -> bool:
    return any(x in lower for x in [
        "new bin",
        "replacement bin",
        "new recycle bin",
        "new recycling bin",
        "recycling container",
        "replacement container",
        "new wheeled bin",
        "new wheeled bins",
        "replacement recycling container",
        "replacement recycling bin",
        "request a new bin",
        "get a new bin",
    ])


def is_new_bin_cost_intent(lower: str) -> bool:
    return is_new_bin_intent(lower) and any(x in lower for x in [
        "cost", "price", "charge", "fee", "how much"
    ])


def is_blue_badge_intent(lower: str) -> bool:
    return "blue badge" in lower


def is_free_school_meals_intent(lower: str) -> bool:
    return "free school meal" in lower or "free school meals" in lower


def is_council_tax_intent(lower: str) -> bool:
    return "council tax" in lower or "c tax" in lower or "ctax" in lower


def is_benefits_intent(lower: str) -> bool:
    return any(x in lower for x in [
        "benefit", "benefits", "universal credit", "housing benefit",
        "pip", "personal independence", "disability living allowance",
        "council tax support", "council tax reduction",
        "free school meal", "free school meals",
    ])


def is_school_intent(lower: str) -> bool:
    return any(x in lower for x in [
        "school", "primary school", "secondary school",
        "admission", "admissions", "school place",
        "year 7", "reception", "school application", "school deadline",
    ])


def is_bins_intent(lower: str) -> bool:
    return any(x in lower for x in [
        "bin", "bins", "rubbish", "recycling", "waste",
        "garden waste", "missed bin", "bin collection",
    ])


def is_context_reset_intent(lower: str) -> bool:
    return any(x in lower for x in [
        "something else",
        "something different",
        "different topic",
        "another question",
        "different question",
        "forget that",
        "start again",
        "new question",
    ])


def resolve_service_hint(question: str, existing_hint: str) -> str:
    lower = normalize(question)

    if is_blue_badge_intent(lower):
        return "Benefits & Support"

    if is_free_school_meals_intent(lower):
        return "Benefits & Support"

    if is_council_tax_intent(lower):
        return "Council Tax"

    if is_collection_date_intent(lower) or is_new_bin_intent(lower):
        return "Waste & Bins"

    if existing_hint:
        return existing_hint

    return "Unknown"


def run_agent(req):
    question = req.question or ""
    service_hint = req.service_hint or ""
    history = req.history if req.history else []
    lower = normalize(question)

    # =========================
    # 0. CONTEXT RESET LANGUAGE
    # =========================
    if is_context_reset_intent(lower):
        return {
            "answer": "Of course. What would you like to ask about next?",
            "service": "Unknown",
            "action": "answer",
            "needs_clarification": False,
            "tool_used": "",
            "next_steps_url": ""
        }

    # =========================
    # 1. BIN COLLECTION DATE FLOW ONLY
    # =========================
    if is_collection_date_intent(lower):
        if looks_like_postcode(question):
            postcode = extract_postcode(question)
            return {
                "answer": f"POSTCODE_LOOKUP::{postcode}",
                "service": "Waste & Bins",
                "action": "tool",
                "needs_clarification": False,
                "tool_used": "postcode_lookup",
                "next_steps_url": ""
            }

        return {
            "answer": "Please enter your postcode so I can check your bin collection details.",
            "service": "Waste & Bins",
            "action": "clarify",
            "needs_clarification": True,
            "tool_used": "",
            "next_steps_url": ""
        }

    # =========================
    # 2. SERVICE HINT RESOLUTION
    # =========================
    resolved_service_hint = resolve_service_hint(question, service_hint)

    # =========================
    # 3. TARGETED RAG SEARCH
    # =========================
    rag = rag_search_tool(question, resolved_service_hint, history=history)

    if rag["answer"]:
        return {
            "answer": rag["answer"],
            "service": rag["service"] or resolved_service_hint,
            "action": "answer",
            "needs_clarification": False,
            "tool_used": "rag",
            "next_steps_url": rag["nextStepsUrl"]
        }

    # =========================
    # 4. SECOND PASS WITHOUT SERVICE HINT
    # =========================
    if resolved_service_hint not in ("", "Unknown"):
        rag = rag_search_tool(question, "", history=history)

        if rag["answer"]:
            return {
                "answer": rag["answer"],
                "service": rag["service"] or resolved_service_hint,
                "action": "answer",
                "needs_clarification": False,
                "tool_used": "rag",
                "next_steps_url": rag["nextStepsUrl"]
            }

    # =========================
    # 5. SMART FALLBACKS
    # =========================
    if is_new_bin_cost_intent(lower):
        return {
            "answer": "Are you asking about the cost of a replacement bin, or would you like to request one? I can help with either.",
            "service": "Waste & Bins",
            "action": "clarify",
            "needs_clarification": True,
            "tool_used": "",
            "next_steps_url": ""
        }

    if is_new_bin_intent(lower):
        return {
            "answer": "Are you looking to get a replacement bin, report a missing bin, or find out about collection days? Let me know and I'll point you in the right direction.",
            "service": "Waste & Bins",
            "action": "clarify",
            "needs_clarification": True,
            "tool_used": "",
            "next_steps_url": ""
        }

    if is_blue_badge_intent(lower):
        return {
            "answer": "I can help with Blue Badge — are you asking about eligibility, how to apply, or what evidence you'll need to provide?",
            "service": "Benefits & Support",
            "action": "clarify",
            "needs_clarification": True,
            "tool_used": "",
            "next_steps_url": ""
        }

    if is_council_tax_intent(lower):
        return {
            "answer": "Are you asking about paying your Council Tax, checking your balance, applying for a discount, or getting support if you're struggling to pay?",
            "service": "Council Tax",
            "action": "clarify",
            "needs_clarification": True,
            "tool_used": "",
            "next_steps_url": ""
        }

    if is_benefits_intent(lower):
        return {
            "answer": "Are you asking about eligibility for a benefit, how to apply, or what evidence you'll need to provide?",
            "service": "Benefits & Support",
            "action": "clarify",
            "needs_clarification": True,
            "tool_used": "",
            "next_steps_url": ""
        }

    if is_school_intent(lower):
        return {
            "answer": "Are you asking about applying for a school place, upcoming admission deadlines, or finding schools near you?",
            "service": "Schools & Education",
            "action": "clarify",
            "needs_clarification": True,
            "tool_used": "",
            "next_steps_url": ""
        }

    if is_bins_intent(lower):
        return {
            "answer": "Are you asking about your bin collection day, reporting a missed bin, or requesting a new or replacement bin?",
            "service": "Waste & Bins",
            "action": "clarify",
            "needs_clarification": True,
            "tool_used": "",
            "next_steps_url": ""
        }

    return {
        "answer": "Could you tell me a bit more about what you need help with? For example, are you asking about council tax, housing, bins, benefits, or schools?",
        "service": resolved_service_hint or "Unknown",
        "action": "clarify",
        "needs_clarification": True,
        "tool_used": "",
        "next_steps_url": ""
    }