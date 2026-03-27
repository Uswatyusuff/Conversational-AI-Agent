import os
import re
from prompts import SYSTEM_PROMPT
from tools import lookup_addresses_by_postcode, lookup_bin_result, read_council_webpage
from langchain_openai import ChatOpenAI


def build_llm():
    return ChatOpenAI(
        model=os.getenv("OPENAI_MODEL", "gpt-4o-mini"),
        temperature=0.2,
        api_key=os.getenv("OPENAI_API_KEY")
    )


def build_context(context_chunks):
    if not context_chunks:
        return "No retrieved context available."

    parts = []
    for i, c in enumerate(context_chunks, start=1):
        parts.append(
            f"[Chunk {i}]\n"
            f"Title: {c.title}\n"
            f"Text: {c.text}\n"
            f"Next URL: {c.nextUrl}"
        )
    return "\n\n".join(parts)


def build_history(history):
    if not history:
        return "No prior history."

    parts = []
    for h in history:
        role = getattr(h, "role", "user")
        msg = getattr(h, "message", "")
        parts.append(f"{role}: {msg}")
    return "\n".join(parts)


def extract_postcode(text: str) -> str:
    # Simple UK postcode matcher
    pattern = r"\b[A-Z]{1,2}\d[A-Z\d]?\s*\d[A-Z]{2}\b"
    match = re.search(pattern, text.upper())
    return match.group(0).strip() if match else ""


def should_use_address_lookup(question: str) -> bool:
    q = question.lower()
    return (
        "postcode" in q or
        "bin collection" in q or
        "bin day" in q or
        "address lookup" in q or
        "find address" in q
    )


def should_use_webpage_reader(question: str) -> bool:
    q = question.lower()
    return "read webpage" in q or "check website" in q or "open page" in q


def run_agent(req):
    llm = build_llm()

    question = req.question.strip()
    service_hint = req.service_hint or "Unknown"
    context_text = build_context(req.context_chunks)
    history_text = build_history(req.history)

    tool_used = ""
    tool_output = ""

    # Tool route 1: postcode / address / bin-related
    postcode = extract_postcode(question)
    if postcode and should_use_address_lookup(question):
        tool_used = "lookup_addresses_by_postcode"
        tool_output = lookup_addresses_by_postcode.invoke(postcode)

    # Tool route 2: explicit webpage reading
    elif should_use_webpage_reader(question):
        # crude URL extraction
        url_match = re.search(r"https?://\S+", question)
        if url_match:
            tool_used = "read_council_webpage"
            tool_output = read_council_webpage.invoke(url_match.group(0))

    prompt = f"""
{SYSTEM_PROMPT}

Service hint:
{service_hint}

Conversation history:
{history_text}

Retrieved council context:
{context_text}

Tool used:
{tool_used if tool_used else "None"}

Tool output:
{tool_output if tool_output else "None"}

User question:
{question}

Instructions:
- If the tool output is useful, use it.
- Otherwise use the retrieved council context.
- If both are weak, ask one short clarification question.
- Return plain text only.
"""

    answer = llm.invoke(prompt).content.strip()

    next_steps_url = ""
    if req.context_chunks:
        first_with_url = next((c for c in req.context_chunks if getattr(c, "nextUrl", "")), None)
        if first_with_url:
            next_steps_url = first_with_url.nextUrl

    return {
        "answer": answer,
        "service": service_hint,
        "action": "answer",
        "needs_clarification": False,
        "tool_used": tool_used,
        "next_steps_url": next_steps_url
    }