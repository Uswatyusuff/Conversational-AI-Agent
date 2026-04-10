import json
import os
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

    if "bin collection" in blob or "collection dates" in blob:
        return "bin_collection"

    if "planning" in blob or "planning application" in blob:
        return "planning"

    if "library" in blob or "libraries" in blob or "renewing borrowing" in blob:
        return "libraries"

    if "housing" in blob or "homeless" in blob:
        return "housing"

    if "school admissions" in blob:
        return "school_admissions"

    return "general"

def normalize_text(value: str) -> str:
    return " ".join((value or "").lower().split())


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
        "analytics on off",
        "marketing on off",
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


def print_debug_summary(pages: list[dict]) -> None:
    print(f"Relevant pages selected: {len(pages)}")

    service_counts = Counter((p.get("service") or "Unknown") for p in pages)
    print("Pages by service:")
    for service, count in service_counts.most_common():
        print(f"  - {service}: {count}")

    important_matches = [
        p for p in pages
        if "wheeled-bins-and-recycling-containers" in (p.get("url") or "").lower()
        or "get-new-wheeled-bins-or-recycling-containers" in (p.get("url") or "").lower()
        or "replacement-bins" in (p.get("url") or "").lower()
    ]

    print(f"Important waste-container pages kept: {len(important_matches)}")
    for page in important_matches[:10]:
        print(f"  - {page.get('url', '')}")


def main():
    if not os.path.exists(INPUT_FILE):
        raise FileNotFoundError(f"{INPUT_FILE} not found. Run scrape_bradford.py first.")

    with open(INPUT_FILE, "r", encoding="utf-8") as f:
        pages = json.load(f)

    pages = dedupe_pages(pages)
    pages = [p for p in pages if is_relevant_page(p)]

    print_debug_summary(pages)

    splitter = RecursiveCharacterTextSplitter(
        chunk_size=1000,
        chunk_overlap=150
    )

    texts = []
    metadatas = []

    for page in pages:
        title = page.get("title", "")
        url = page.get("url", "")
        service = page.get("service", "Unknown")
        text = page.get("text", "")

        if not text or not text.strip():
            continue

        chunks = splitter.split_text(text)

        for chunk in chunks:
            cleaned_chunk = chunk.strip()
            if not is_good_chunk(cleaned_chunk):
                continue

            topic = detect_topic(title, url, cleaned_chunk)

            texts.append(
                f"TITLE: {title}\n"
                f"URL: {url}\n"
                f"SERVICE: {service}\n"
                f"TOPIC: {topic}\n\n"
                f"{cleaned_chunk}"
                )
            metadatas.append({
                "title": title,
                "url": url,
                "service": service,
                "topic": topic,
                "seed_group": page.get("seed_group", "")
            })


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