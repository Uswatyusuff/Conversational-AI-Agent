import json
import os
import time
import hashlib
import requests
import xml.etree.ElementTree as ET
from collections import deque
from urllib.parse import urljoin, urlparse, urlunparse

from bs4 import BeautifulSoup
from playwright.sync_api import sync_playwright

BASE_URL = "https://www.bradford.gov.uk/"
BASE_DOMAIN = "www.bradford.gov.uk"
OUTPUT_FILE = "data/bradford_pages.json"

MAX_PAGES = 2500
REQUEST_TIMEOUT = 30
visited_urls = set()
queued_urls = set()
seen_text_hashes = set()
pages = []


def infer_service(url: str, title: str, text: str) -> str:
    blob = f"{url} {title} {text}".lower()

    if any(x in blob for x in ["council tax", "ctax"]):
        return "Council Tax"

    if any(x in blob for x in [
        "bins", "waste", "recycling", "missed bin", "collection",
        "replacement bin", "new bin", "bulky waste", "garden waste"
    ]):
        return "Waste & Bins"

    if any(x in blob for x in [
        "benefit", "housing benefit", "blue badge", "support",
        "council tax reduction", "free school meals"
    ]):
        return "Benefits & Support"

    if any(x in blob for x in [
        "school", "admission", "education", "primary school",
        "secondary school", "school place"
    ]):
        return "Education"

    return "Unknown"


def normalize_url(url: str) -> str:
    if not url:
        return ""

    try:
        parsed = urlparse(url.strip())

        if parsed.scheme not in ("http", "https"):
            return ""

        if not parsed.netloc.endswith(BASE_DOMAIN):
            return ""

        # remove query + fragment
        cleaned = parsed._replace(query="", fragment="")
        normalized = urlunparse(cleaned).rstrip("/")

        return normalized
    except Exception:
        return ""


def is_valid_bradford_url(url: str) -> bool:
    normalized = normalize_url(url)
    return bool(normalized)


def is_html_candidate(url: str) -> bool:
    lower = url.lower()
    blocked_extensions = (
        ".pdf", ".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg",
        ".doc", ".docx", ".xls", ".xlsx", ".csv", ".zip", ".mp3", ".mp4"
    )
    blocked_fragments = [
        "javascript:",
        "mailto:",
        "tel:"
    ]

    if lower.endswith(blocked_extensions):
        return False

    if any(lower.startswith(x) for x in blocked_fragments):
        return False

    return True


def should_skip_url(url: str) -> bool:
    lower = url.lower()

    skip_patterns = [
        "/search",
        "/privacy",
        "/cookies",
        "/accessibility",
        "/a-to-z",
        "/contact-us-online",
        "/favicon.ico"
    ]

    return any(x in lower for x in skip_patterns)


def fetch_text(url: str) -> str:
    response = requests.get(
        url,
        timeout=REQUEST_TIMEOUT,
        headers={"User-Agent": "Mozilla/5.0"}
    )
    response.raise_for_status()
    return response.text


def clean_html_text(html: str) -> str:
    soup = BeautifulSoup(html, "html.parser")

    for tag in soup(["script", "style", "noscript", "svg"]):
        tag.decompose()

    text = soup.get_text(separator="\n", strip=True)
    return clean_text(text)


def clean_text(text: str) -> str:
    bad_phrases = [
        "skip to main content",
        "cookies",
        "privacy notice",
        "accessibility",
        "a to z",
        "register | log on",
        "sign up for stay connected bulletins",
        "bradford council online services"
    ]

    lines = []
    for raw in text.splitlines():
        line = " ".join(raw.split()).strip()
        if not line:
            continue
        if len(line) < 20:
            continue
        if any(bad in line.lower() for bad in bad_phrases):
            continue
        lines.append(line)

    return "\n".join(lines).strip()


def text_hash(text: str) -> str:
    return hashlib.md5(text.encode("utf-8")).hexdigest()


def discover_sitemaps() -> list[str]:
    robots_url = urljoin(BASE_URL, "/robots.txt")
    found = []

    try:
        robots_text = fetch_text(robots_url)
        for line in robots_text.splitlines():
            if line.lower().startswith("sitemap:"):
                sitemap_url = line.split(":", 1)[1].strip()
                found.append(sitemap_url)
    except Exception as ex:
        print(f"Could not read robots.txt: {ex}")

    fallback = [
        urljoin(BASE_URL, "/sitemap.xml"),
        urljoin(BASE_URL, "/sitemap_index.xml")
    ]

    for item in fallback:
        if item not in found:
            found.append(item)

    return list(dict.fromkeys(found))


def parse_sitemap(sitemap_url: str, seen_sitemaps: set[str] | None = None) -> set[str]:
    if seen_sitemaps is None:
        seen_sitemaps = set()

    urls = set()

    if sitemap_url in seen_sitemaps:
        return urls

    seen_sitemaps.add(sitemap_url)

    try:
        xml_text = fetch_text(sitemap_url)
        root = ET.fromstring(xml_text)

        namespace = ""
        if root.tag.startswith("{"):
            namespace = root.tag.split("}")[0] + "}"

        sitemap_nodes = root.findall(f".//{namespace}sitemap/{namespace}loc")
        if sitemap_nodes:
            for node in sitemap_nodes:
                child = (node.text or "").strip()
                if child:
                    urls.update(parse_sitemap(child, seen_sitemaps))
            return urls

        url_nodes = root.findall(f".//{namespace}url/{namespace}loc")
        for node in url_nodes:
            page_url = normalize_url((node.text or "").strip())
            if page_url and is_html_candidate(page_url) and not should_skip_url(page_url):
                urls.add(page_url)

    except Exception as ex:
        print(f"Could not parse sitemap {sitemap_url}: {ex}")

    return urls


def extract_links_from_html(html: str, current_url: str) -> set[str]:
    links = set()
    soup = BeautifulSoup(html, "html.parser")

    for a in soup.select("a[href]"):
        href = a.get("href", "").strip()
        if not href:
            continue

        full = normalize_url(urljoin(current_url, href))
        if full and is_html_candidate(full) and not should_skip_url(full):
            links.add(full)

    return links


def click_expandable_buttons(page):
    selectors = [
        "button",
        "[role='button']",
        "summary"
    ]

    phrases = [
        "show more",
        "read more",
        "expand",
        "open",
        "more",
        "details",
        "view more"
    ]

    for selector in selectors:
        try:
            elements = page.locator(selector)
            count = min(elements.count(), 60)

            for i in range(count):
                try:
                    el = elements.nth(i)
                    text = (el.inner_text(timeout=500) or "").strip().lower()

                    if not text:
                        continue

                    if any(p in text for p in phrases):
                        el.click(timeout=1000)
                        time.sleep(0.15)
                except Exception:
                    pass
        except Exception:
            pass


def extract_links_from_page_dom(page, current_url: str) -> set[str]:
    found = set()

    try:
        links = page.locator("a[href]")
        count = min(links.count(), 400)

        for i in range(count):
            try:
                href = links.nth(i).get_attribute("href", timeout=500)
                if not href:
                    continue

                full = normalize_url(urljoin(current_url, href))
                if full and is_html_candidate(full) and not should_skip_url(full):
                    found.add(full)
            except Exception:
                pass
    except Exception:
        pass

    return found


def build_queue() -> deque[str]:
    queue = deque()

    sitemap_urls = discover_sitemaps()
    discovered = set()

    for sitemap_url in sitemap_urls:
        print(f"Reading sitemap: {sitemap_url}")
        discovered.update(parse_sitemap(sitemap_url))

    # Add homepage and core areas as fallback / priority
    priority_urls = [
        "https://www.bradford.gov.uk/",
        "https://www.bradford.gov.uk/bins-recycling-and-waste/",
        "https://www.bradford.gov.uk/council-tax/",
        "https://www.bradford.gov.uk/benefits/",
        "https://www.bradford.gov.uk/education-and-skills/",
    ]

    ordered = []
    for url in priority_urls:
        norm = normalize_url(url)
        if norm:
            ordered.append(norm)

    ordered.extend(sorted(discovered))

    for url in ordered:
        if url not in queued_urls:
            queue.append(url)
            queued_urls.add(url)

    print(f"Initial queue size: {len(queue)}")
    return queue


def scrape():
    queue = build_queue()

    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        page = browser.new_page()

        while queue and len(visited_urls) < MAX_PAGES:
            url = queue.popleft()

            if url in visited_urls:
                continue

            visited_urls.add(url)
            print(f"[{len(visited_urls)}/{MAX_PAGES}] Scraping: {url}")

            try:
                static_html = ""
                static_links = set()

                # Static fetch first
                try:
                    static_html = fetch_text(url)
                    static_links = extract_links_from_html(static_html, url)

                    for link in static_links:
                        if link not in visited_urls and link not in queued_urls:
                            queue.append(link)
                            queued_urls.add(link)
                except Exception:
                    pass

                # Browser-rendered version
                page.goto(url, timeout=60000)
                page.wait_for_load_state("networkidle")
                time.sleep(0.5)

                click_expandable_buttons(page)

                title = page.title()
                body_text = page.locator("body").inner_text(timeout=5000)
                cleaned = clean_text(body_text)

                dom_links = extract_links_from_page_dom(page, url)
                for link in dom_links:
                    if link not in visited_urls and link not in queued_urls:
                        queue.append(link)
                        queued_urls.add(link)

                if len(cleaned) > 200:
                    hashed = text_hash(cleaned[:5000])

                    if hashed not in seen_text_hashes:
                        seen_text_hashes.add(hashed)

                        pages.append({
                            "url": url,
                            "title": title.strip() if title else url,
                            "service": infer_service(url, title or "", cleaned),
                            "text": cleaned[:20000]
                        })

            except Exception as ex:
                print(f"Failed {url}: {ex}")

        browser.close()


if __name__ == "__main__":
    os.makedirs("data", exist_ok=True)

    scrape()

    with open(OUTPUT_FILE, "w", encoding="utf-8") as f:
        json.dump(pages, f, indent=2, ensure_ascii=False)

    print(f"Saved {len(pages)} pages to {OUTPUT_FILE}")
    print(f"Visited {len(visited_urls)} URLs")