import json
import os
import re
from collections import Counter

from dotenv import load_dotenv
from langchain_text_splitters import RecursiveCharacterTextSplitter
from langchain_community.vectorstores import FAISS

from embedding_client import ExternalEmbeddingService

load_dotenv()

# INPUT_FILE = "data/bradford_pages.json"
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
INPUT_FILE = os.path.join(BASE_DIR, "data", "bradford_targeted_pages.json")
INDEX_DIR = os.path.join(BASE_DIR, "data", "faiss_index")


INCLUDE_KEYWORDS = [
    "council-tax",
    "bins",
    "waste",
    "recycling",
    "benefits",
    "blue-badge",
    "free-school-meals",
    "education",
    "school",
    "admissions",
    "wheeled-bins",
    "wheeled-bins-and-recycling-containers",
    "recycling-containers",
    "get-new-wheeled-bins",
    "get-new-wheeled-bins-or-recycling-containers",
    "replacement-bins",
    "new bin",
    "replacement bin",
    "garden-waste",
    "missed-bin",
]

EXCLUDE_KEYWORDS = [
    "scrutiny",
    "committee",
    "lord-mayor",
    "archive",
    "your-council",
    "civic-protocol",
    "councillors",
    "council-meetings",
    "mayors-and-lord-mayors",
    "statement-of-accounts",
    "budget",
    "elections",
    "democracy",
    "bank-holiday-closure-times",
    "bank-holiday",
    "closure-times",
    "news",
    "events",
]

FORCE_INCLUDE_URL_PARTS = [
    "wheeled-bins-and-recycling-containers",
    "get-new-wheeled-bins-or-recycling-containers",
    "replacement-bins",
    "recycling-containers",
    "check-your-bin-collection-dates",
    "blue-badge",
    "free-school-meals",
    "school-admissions",
    "council-tax","blue-badge-scheme",
    "housing-benefit",
    "benefits-faqs",
    "proof-you-need-to-provide",
    "free-school-meals","apply-for-a-place",
    "blue-badge",
    "blue-badge-scheme",
]

FORCE_EXCLUDE_URL_PARTS = [
    "scrap-metal-dealers-licence",
    "404-error-page",
    "bank-holiday-closure-times",
    "adult-shop-adult-cinema-and-adult-entertainment-venues",
    "site-navigation",
    "privacy-notice",
    "accessibility",
    "cookies",
]
def detect_topic(title: str, url: str, text: str) -> str:
    blob = f"{title} {url} {text[:3000]}".lower()

    if "blue badge" in blob:
        return "blue_badge"

    if "free school meals" in blob:
        return "free_school_meals"

    if "housing benefit" in blob:
        return "housing_benefit"

    if "council tax reduction" in blob:
        return "council_tax_reduction"

    if "council tax" in blob:
        return "council_tax"

    if (
        "get-new-wheeled-bins-or-recycling-containers" in blob
        or "new wheeled bins" in blob
        or "replacement bin" in blob
        or "replacement container" in blob
        or "recycling container" in blob
        or "new recycling bin" in blob
        or "new bin" in blob
    ):
        return "new_bin"

    # missed_bin before generic bin_collection so specific intent wins
    if "missed bin" in blob or "missed collection" in blob or "bin not collected" in blob:
        return "missed_bin"

    # garden_waste before generic bin_collection
    if "garden waste" in blob or "garden-waste" in blob:
        return "garden_waste"

    if "bin collection" in blob or "collection dates" in blob:
        return "bin_collection"

    # school_admissions before generic housing/planning so specific intent wins
    if (
        "school admissions" in blob
        or "apply for a place" in blob
        or "apply for a school" in blob
        or "school-admissions" in blob
    ):
        return "school_admissions"

    if "planning" in blob or "planning application" in blob:
        return "planning"

    if "library" in blob or "libraries" in blob or "renewing borrowing" in blob:
        return "libraries"

    if "housing" in blob or "homeless" in blob or "homelessness" in blob:
        return "housing"

    return "general"

def normalize_text(value: str) -> str:
    return " ".join((value or "").lower().split())


# ── Boilerplate stripping ─────────────────────────────────────────────────────

_BOILERPLATE_EXACT = {
    "skip to main content", "back to top", "was this page helpful",
    "yes no", "print this page", "share this page",
    "register | log on", "bradford council online services",
    "analytics on off", "marketing on off", "sign up for email alerts",
    "a to z of services", "follow us on", "twitter facebook youtube",
    "contact us now cookies privacy notice",
    "copyright 2026 city of bradford metropolitan district council",
    "copyright city of bradford metropolitan district council",
}


def strip_boilerplate(text: str) -> str:
    """Remove known nav/footer boilerplate lines from raw page text."""
    lines = text.splitlines()
    cleaned = []
    for line in lines:
        norm = line.strip().lower()
        if norm in _BOILERPLATE_EXACT:
            continue
        if len(line.strip()) < 3:
            continue
        cleaned.append(line)
    return "\n".join(cleaned)


# ── FAQ density detection ─────────────────────────────────────────────────────

def is_faq_heavy(text: str) -> bool:
    """Return True when a page has many question-style lines (≥ 4)."""
    lines = text.splitlines()
    q_lines = sum(
        1 for l in lines
        if l.strip().endswith("?") or l.strip().lower().startswith("q:")
    )
    return q_lines >= 4


# ── Heading-aware section splitting ──────────────────────────────────────────

_HEADING_RE = re.compile(r"^#{1,3}\s+\S")  # markdown ## headings


def _looks_like_heading(line: str) -> bool:
    """
    Heuristic: a heading is a short, capitalised line with no terminal punctuation.
    Conservative thresholds to minimise false positives on scraped plain text.
    """
    s = line.strip()
    if not s or len(s) < 4:
        return False
    # Explicit markdown headings (if scraper emits them)
    if _HEADING_RE.match(s):
        return True
    # Short title-like lines: 2–10 words, starts uppercase, no sentence-ending punct
    if len(s) <= 65 and s[0].isupper() and s[-1] not in (".", ",", ";",":"):
        words = s.split()
        if 2 <= len(words) <= 10:
            # Not a list item or numbered step
            if s[0] not in ("-", "*", "•", "–", "·", ">") and not re.match(r"^\d+[\.\)]\s", s):
                return True
    return False


def extract_sections(text: str) -> list[tuple[str, str]]:
    """
    Split page text into (heading, body) pairs.
    - An empty heading means content before the first heading.
    - Degrades gracefully: if no headings are found, returns [('' , full_text)].
    """
    lines = text.splitlines()
    sections: list[tuple[str, str]] = []
    current_heading = ""
    current_lines: list[str] = []

    for line in lines:
        if _looks_like_heading(line):
            if current_lines:
                body = "\n".join(current_lines).strip()
                if body:
                    sections.append((current_heading, body))
            current_heading = line.strip().lstrip("#").strip()
            current_lines = []
        else:
            current_lines.append(line)

    if current_lines:
        body = "\n".join(current_lines).strip()
        if body:
            sections.append((current_heading, body))

    return sections or [("", text.strip())]


# ── Chunk type classification ─────────────────────────────────────────────────

def detect_chunk_type(chunk: str) -> str:
    """Return 'faq', 'list', or 'standard' based on chunk content."""
    lines = [l.strip() for l in chunk.splitlines() if l.strip()]
    if not lines:
        return "standard"
    q_count = sum(1 for l in lines if l.endswith("?"))
    list_count = sum(
        1 for l in lines
        if l.startswith(("-", "*", "•", "–")) or re.match(r"^\d+[\.\)]\s", l)
    )
    if q_count >= 2 or (q_count >= 1 and len(lines) <= 6):
        return "faq"
    if list_count >= 3:
        return "list"
    return "standard"


def is_relevant_page(page: dict) -> bool:
    url = normalize_text(page.get("url", ""))
    title = normalize_text(page.get("title", ""))
    service = normalize_text(page.get("service", ""))
    text = normalize_text(page.get("text", ""))

    blob = f"{url} {title} {service} {text[:3000]}"

    if any(x in url for x in FORCE_EXCLUDE_URL_PARTS):
        return False

    if any(x in url for x in FORCE_INCLUDE_URL_PARTS):
        return True

    if any(x in url for x in EXCLUDE_KEYWORDS) or any(x in title for x in EXCLUDE_KEYWORDS):
        return False
    
    if service in {
        "council tax",
        "waste & bins",
        "benefits & support",
        "education",
        "housing",
        "libraries",
        "planning",
        "contact us",
    }:
        return True
    

    return any(x in blob for x in INCLUDE_KEYWORDS)


def is_good_chunk(chunk: str) -> bool:
    cleaned = chunk.strip()
    if len(cleaned) < 80:
        return False

    bad_chunk_signals = [
        "skip to main content",
        "privacy notice",
        "cookies",
        "accessibility",
        "a to z",
        "register | log on",
        "bradford council online services",
        "back to top",
        "was this page helpful",
        "yes no",
        "print this page",
        "share this page",
        "contact us now cookies privacy notice",
        "copyright 2026 city of bradford metropolitan district council",
        "copyright city of bradford metropolitan district council",
        "analytics on off",
        "marketing on off",
        "sign up for email alerts",
        "follow us on",
        "twitter facebook",
        "you are here:",
        "breadcrumb navigation",
        "last updated:",
    ]

    lower = cleaned.lower()
    if any(x in lower for x in bad_chunk_signals):
        return False

    return True


def dedupe_pages(pages: list[dict]) -> list[dict]:
    seen_urls = set()
    output = []

    for page in pages:
        url = (page.get("url") or "").strip().rstrip("/")
        if not url or url in seen_urls:
            continue

        seen_urls.add(url)
        output.append(page)

    return output


def dedupe_chunks(
    texts: list[str],
    metadatas: list[dict],
) -> tuple[list[str], list[dict]]:
    """
    Remove chunks whose body content is a near-duplicate of an already-seen chunk.
    The fingerprint is built from the body lines only (after the TITLE/URL/etc header),
    so the same content from two different pages is still kept (different metadata).
    """
    seen: set[tuple[str, str]] = set()
    out_texts: list[str] = []
    out_metas: list[dict] = []

    for t, m in zip(texts, metadatas):
        # Isolate body: skip header lines that start with known prefixes
        body_lines = [
            l for l in t.splitlines()
            if not l.startswith(("TITLE:", "URL:", "SERVICE:", "TOPIC:", "HEADING:"))
        ]
        fingerprint = " ".join(" ".join(body_lines).lower().split())
        key = (m.get("url", ""), fingerprint)   # same URL + same body = duplicate
        if key in seen:
            continue
        seen.add(key)
        out_texts.append(t)
        out_metas.append(m)

    return out_texts, out_metas


def print_debug_summary(
    pages: list[dict],
    chunks_before: int = 0,
    chunks_after: int = 0,
    metadatas: list[dict] | None = None,
) -> None:
    sep = "=" * 52
    print(f"\n{sep}")
    print(f"  Pages kept:              {len(pages)}")
    if chunks_before:
        print(f"  Chunks before dedupe:    {chunks_before}")
        print(f"  Chunks after dedupe:     {chunks_after}")
        print(f"  Duplicates removed:      {chunks_before - chunks_after}")
    print(sep)

    service_counts = Counter((p.get("service") or "Unknown") for p in pages)
    print("Pages by service:")
    for service, count in service_counts.most_common():
        print(f"  {service:<35} {count}")

    important_matches = [
        p for p in pages
        if "wheeled-bins-and-recycling-containers" in (p.get("url") or "").lower()
        or "get-new-wheeled-bins-or-recycling-containers" in (p.get("url") or "").lower()
        or "replacement-bins" in (p.get("url") or "").lower()
    ]
    print(f"\nImportant waste-container pages kept: {len(important_matches)}")
    for page in important_matches[:10]:
        print(f"  {page.get('url', '')}")

    if metadatas:
        topic_counts = Counter(m.get("topic", "general") for m in metadatas)
        print("\nTop topics by chunk count:")
        for topic, count in topic_counts.most_common(12):
            print(f"  {topic:<35} {count}")

        ctype_counts = Counter(m.get("chunk_type", "standard") for m in metadatas)
        print("\nChunk types:")
        for ctype, count in ctype_counts.most_common():
            print(f"  {ctype:<35} {count}")

    print(sep)


def main():
    if not os.path.exists(INPUT_FILE):
        raise FileNotFoundError(f"{INPUT_FILE} not found. Run scrape_bradford.py first.")

    with open(INPUT_FILE, "r", encoding="utf-8") as f:
        pages = json.load(f)

    pages = dedupe_pages(pages)
    pages = [p for p in pages if is_relevant_page(p)]

    # Two splitters: smaller chunks for FAQ-heavy pages, normal for everything else
    standard_splitter = RecursiveCharacterTextSplitter(
        chunk_size=1000,
        chunk_overlap=150,
    )
    faq_splitter = RecursiveCharacterTextSplitter(
        chunk_size=500,
        chunk_overlap=60,
    )

    texts: list[str] = []
    metadatas: list[dict] = []

    for page in pages:
        title = page.get("title", "")
        url = page.get("url", "")
        service = page.get("service", "Unknown")
        raw_text = page.get("text", "")

        if not raw_text or not raw_text.strip():
            continue

        # 1. Strip nav/footer boilerplate before any splitting
        clean_text = strip_boilerplate(raw_text)

        # 2. Choose splitter based on FAQ density
        splitter = faq_splitter if is_faq_heavy(clean_text) else standard_splitter

        # 3. Split into heading-delimited sections so heading context stays attached
        sections = extract_sections(clean_text)

        for heading, section_body in sections:
            # Prefix body with heading so the splitter keeps that context in every sub-chunk
            section_text = f"{heading}\n\n{section_body}".strip() if heading else section_body

            chunks = splitter.split_text(section_text)

            for chunk in chunks:
                cleaned_chunk = chunk.strip()
                if not is_good_chunk(cleaned_chunk):
                    continue

                topic = detect_topic(title, url, cleaned_chunk)
                chunk_type = detect_chunk_type(cleaned_chunk)

                # section_hint: heading if present, otherwise first line of chunk (capped)
                section_hint = heading if heading else (cleaned_chunk.splitlines()[0][:80] if cleaned_chunk else "")

                indexed_text = (
                    f"TITLE: {title}\n"
                    f"URL: {url}\n"
                    f"SERVICE: {service}\n"
                    f"TOPIC: {topic}\n"
                    + (f"HEADING: {heading}\n" if heading else "")
                    + f"\n{cleaned_chunk}"
                )

                texts.append(indexed_text)
                metadatas.append({
                    "title": title,
                    "url": url,
                    "service": service,
                    "topic": topic,
                    "seed_group": page.get("seed_group", ""),
                    "heading": heading,
                    "section_hint": section_hint,
                    "chunk_type": chunk_type,
                })

    # 4. Remove duplicate chunks (same URL + same body content)
    chunks_before = len(texts)
    texts, metadatas = dedupe_chunks(texts, metadatas)
    chunks_after = len(texts)

    print_debug_summary(pages, chunks_before, chunks_after, metadatas)

    if not texts:
        raise ValueError("No text chunks were created from filtered Bradford pages.")

    print(f"Creating embeddings for {len(texts)} chunks...")

    embeddings = ExternalEmbeddingService()
    db = FAISS.from_texts(texts, embeddings, metadatas=metadatas)

    os.makedirs(os.path.join(BASE_DIR, "data"), exist_ok=True)
    db.save_local(INDEX_DIR)

    print(f"Saved FAISS index to {INDEX_DIR}")
    print(f"Total chunks indexed: {len(texts)}")


if __name__ == "__main__":
    main()