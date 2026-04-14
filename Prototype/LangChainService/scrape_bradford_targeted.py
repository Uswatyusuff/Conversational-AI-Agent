import json
import os
import re
import time
from collections import deque
from urllib.parse import urljoin, urlparse, urldefrag

from playwright.sync_api import sync_playwright, TimeoutError as PlaywrightTimeoutError

OUTPUT_FILE = "data/bradford_targeted_pages.json"
MAX_DEPTH = 2
MAX_PAGES = 1200

ALLOWED_DOMAINS = {
    "www.bradford.gov.uk",
    "onlineforms.bradford.gov.uk",
}
BLOCKED_URL_PARTS = [
    "/site-navigation",
    "/cookies",
    "/privacy-notice",
    "/our-websites/accessibility",
    "/a-to-z",
    "/contact-us/contact-us-now/my-accounts",
    "/404-error-page",
    "bank-holiday-closure-times",
    "adult-shop-adult-cinema-and-adult-entertainment-venues",
    "scrap-metal-dealers-licence",
]

SEED_GROUPS = {
    
    "Benefits & Blue Badge": [
        "https://www.bradford.gov.uk/benefits/general-benefits-information/proof-you-need-to-provide/",
        "https://www.bradford.gov.uk/benefits/general-benefits-information/benefits-faqs/",
        "https://www.bradford.gov.uk/benefits/general-benefits-information/help-with-cost-of-living/",
        "https://www.bradford.gov.uk/benefits/general-benefits-information/benefits-and-welfare-advice-and-help/",
        "https://www.bradford.gov.uk/benefits/applying-for-benefits/housing-benefit-and-council-tax-reduction/",
        "https://www.bradford.gov.uk/benefits/applying-for-benefits/housing-payment/",
        "https://www.bradford.gov.uk/benefits/applying-for-benefits/free-school-meals/",
        "https://www.bradford.gov.uk/transport-and-travel/transport-for-disabled-people/blue-badge-scheme/",
    ],
    "Council Tax": [
        "https://www.bradford.gov.uk/council-tax/council-tax/",
        "https://www.bradford.gov.uk/paying-for-services/direct-debit-and-paperless-bills/direct-debit/",
        "https://www.bradford.gov.uk/council-tax/report-a-change-of-address-or-circumstances/report-a-change-or-ask-a-question-about-your-council-tax/",
        "https://www.bradford.gov.uk/council-tax/problems-paying-your-bill/behind-on-your-council-tax-payments/",
        "https://www.bradford.gov.uk/council-tax/council-tax-bills/council-tax-bills/",
        "https://www.bradford.gov.uk/benefits/myinfo/myinfo/",
    ],
    "Libraries": [
        "https://www.bradford.gov.uk/libraries/libraries/",
        "https://www.bradford.gov.uk/libraries/library-services-online/renewing-borrowing-and-reserving-items/",
        "https://www.bradford.gov.uk/libraries/library-services-online/e-books/",
        "https://www.bradford.gov.uk/libraries/library-services-online/join-the-library/",
        "https://www.bradford.gov.uk/libraries/library-services-online/digital-library/",
        "https://www.bradford.gov.uk/libraries/find-your-local-library/find-your-local-library/",
    ],
    "Housing": [
        "https://www.bradford.gov.uk/housing/housing/",
        "https://www.bradford.gov.uk/housing/finding-a-home/how-can-i-find-a-home/",
        "https://www.bradford.gov.uk/housing/homelessness/how-to-get-help-if-you-are-homeless/",
        "https://www.bradford.gov.uk/housing/housing-assistance/financial-assistance-for-homeowners-with-home-improvements-and-repairs/",
        "https://www.bradford.gov.uk/housing/advice-for-tenants/getting-repairs-done/",
    ],
    "Recycling & Waste": [
        "https://www.bradford.gov.uk/recycling-and-waste/recycling-and-waste/",
        "https://onlineforms.bradford.gov.uk/ufs/collectiondates.eb?ebd=0&ebp=20&ebz=3_1775591576133",
        "https://www.bradford.gov.uk/recycling-and-waste/wheeled-bins-and-recycling-containers/get-new-wheeled-bins-or-recycling-containers/",
        "https://www.bradford.gov.uk/recycling-and-waste/wheeled-bins-and-recycling-containers/what-goes-in-your-bins/",
        "https://www.bradford.gov.uk/recycling-and-waste/household-waste-recycling-centres/search-household-waste-sites/",
    ],
    "School Admissions": [
        "https://www.bradford.gov.uk/education-and-skills/school-admissions/school-admissions/",
        "https://www.bradford.gov.uk/education-and-skills/school-admissions/about-school-admissions/",
        "https://www.bradford.gov.uk/education-and-skills/school-admissions/admission-of-summer-born-children/",
        "https://www.bradford.gov.uk/education-and-skills/school-admissions/apply-for-a-place-at-one-of-bradford-districts-schools/",
        "https://www.bradford.gov.uk/education-and-skills/school-admissions/admission-arrangements/",
        "https://www.bradford.gov.uk/education-and-skills/school-meals/primary-schools/",
        "https://www.bradford.gov.uk/education-and-skills/school-meals/secondary-schools/",
    ],
    "Contact Us": [
        "https://www.bradford.gov.uk/contact-us/contact-us-now/contact-us-now/",
        "https://www.bradford.gov.uk/contact-us/contact-us-by-telephone/contact-us-by-telephone/",
        "https://www.bradford.gov.uk/contact-us/email-alerts/stay-connected-sign-up-for-email-alerts/",
    ],
    "Planning": [
        "https://www.bradford.gov.uk/planning-and-building-control/planning-and-building-control/",
        "https://www.bradford.gov.uk/planning-and-building-control/planning-application-and-building-regulations-advice/do-i-need-planning-permission-advice-for-householders/",
        "https://www.bradford.gov.uk/planning-and-building-control/planning-applications/view-planning-applications/",
        "https://www.bradford.gov.uk/planning-and-building-control/planning-applications/comment-on-or-object-to-a-planning-application/",
        "https://www.bradford.gov.uk/planning-and-building-control/planning-applications/make-a-planning-application/",
        "https://www.bradford.gov.uk/transport-and-travel/parking/blue-badge-parking/",
    ],
}


def normalize_url(url: str) -> str:
    url = urldefrag(url)[0].strip()
    if url.endswith("/") and len(url) > len("https://x.co/"):
        url = url.rstrip("/")
    return url


def is_allowed_url(url: str) -> bool:
    try:
        parsed = urlparse(url)
        if parsed.scheme not in ("http", "https"):
            return False
        if parsed.netloc not in ALLOWED_DOMAINS:
            return False

        lower = url.lower()

        blocked_exts = (
    ".jpg", ".jpeg", ".png", ".gif", ".svg", ".webp",
    ".mp4", ".mp3", ".zip", ".doc", ".docx", ".xls", ".xlsx", ".pdf"
)
        if lower.endswith(blocked_exts):
            return False

        if any(part in lower for part in BLOCKED_URL_PARTS):
            return False

        return True
    except Exception:
        return False

def clean_text(text: str) -> str:
    text = re.sub(r"\s+", " ", text or "").strip()
    return text

def strip_bradford_boilerplate(text: str) -> str:
    if not text:
        return ""

    bad_phrases = [
        "This site uses cookies to store information on your computer.",
        "I Accept CookiesI Do Not Accept Cookies",
        "Necessary Cookies",
        "Necessary cookies enable core functionality such as page navigation and access to secure areas.",
        "Analytical Cookies",
        "Analytical cookies help us to improve our website by collecting and reporting information on its usage.",
        "Marketing Cookies",
        "We use marketing cookies to help us improve the relevancy of advertising campaigns you receive.",
        "About this tool (Opens in a new window)",
        "skip to main content",
        "Register | Log on",
        "Contact us now Cookies Privacy Notice A to Z Accessibility Statement",
        "Cookies Privacy Notice A to Z Accessibility Statement",
        "Copyright 2026 City of Bradford Metropolitan District Council",
        "marketing On Off",
        "Analytics On Off",
        "Necessary cookies enable core functionality",
        "Register | Log on Bradford Council",
        "Contact us now",
        "Privacy Notice",
        "A to Z",
        "Accessibility Statement",
    ]

    cleaned = text
    for phrase in bad_phrases:
        cleaned = cleaned.replace(phrase, " ")

    cleaned = re.sub(r"\s+", " ", cleaned).strip()
    return cleaned

def detect_service_group(url: str, title: str, seed_group: str) -> str:
    blob = f"{url} {title} {seed_group}".lower()

    if "council-tax" in blob or "council tax" in blob:
        return "Council Tax"
    if "blue badge" in blob or "benefit" in blob:
        return "Benefits & Support"
    if "school" in blob or "admission" in blob or "education" in blob:
        return "Education"
    if "recycling" in blob or "waste" in blob or "bin" in blob or "recycling-containers" in blob:
        return "Waste & Bins"
    if "housing" in blob or "homeless" in blob:
        return "Housing"
    if "library" in blob:
        return "Libraries"
    if "planning" in blob:
        return "Planning"
    if "contact-us" in blob or "contact us" in blob:
        return "Contact Us"

    return seed_group

def click_expandable_elements(page):
    selectors = [
        "button",
        "[role='button']",
        "summary",
        "details summary",
        ".accordion button",
        ".govuk-accordion__section-button",
        "[aria-expanded='false']",
        "[data-testid*='accordion']",
        "[class*='accordion'] button",
        "[class*='collapse'] button",
        "[class*='expand'] button",
        "a.button",
        "a.btn",
        ".btn",
    ]

    clicked = 0
    seen_labels = set()

    for selector in selectors:
        try:
            elements = page.locator(selector)
            count = min(elements.count(), 80)
            for i in range(count):
                el = elements.nth(i)
                try:
                    if not el.is_visible():
                        continue

                    label = clean_text(el.inner_text(timeout=500))
                    aria = (el.get_attribute("aria-label") or "").strip()
                    text_key = (label or aria or f"{selector}_{i}").lower()

                    if text_key in seen_labels:
                        continue

                    interesting = any(k in text_key for k in [
                        "show", "more", "expand", "open", "view",
                        "faq", "question", "answer", "details",
                        "apply", "contact", "read", "continue",
                        "next", "find out", "see"
                    ]) or selector in ("summary", "details summary")

                    if not interesting:
                        expanded = (el.get_attribute("aria-expanded") or "").lower()
                        if expanded != "false":
                            continue

                    seen_labels.add(text_key)
                    el.scroll_into_view_if_needed(timeout=1000)
                    el.click(timeout=1200, force=False)
                    page.wait_for_timeout(250)
                    clicked += 1
                except Exception:
                    continue
        except Exception:
            continue

    return clicked


def extract_links(page, current_url: str):
    found = []
    try:
        anchors = page.locator("a[href]")
        count = min(anchors.count(), 400)
        for i in range(count):
            try:
                href = anchors.nth(i).get_attribute("href")
                if not href:
                    continue
                full = normalize_url(urljoin(current_url, href))
                if is_allowed_url(full):
                    found.append(full)
            except Exception:
                continue
    except Exception:
        pass

    return sorted(set(found))


def scrape_page(page, url: str, seed_group: str):
    page.goto(url, wait_until="domcontentloaded", timeout=45000)

    try:
        page.wait_for_load_state("networkidle", timeout=5000)
    except Exception:
        pass
    page.wait_for_timeout(1200)

    # Try to accept cookies if present
    cookie_selectors = [
        "button:has-text('Accept')",
        "button:has-text('I agree')",
        "button:has-text('Allow all')",
        "#onetrust-accept-btn-handler",
    ]
    for selector in cookie_selectors:
        try:
            btn = page.locator(selector).first
            if btn.count() > 0 and btn.is_visible():
                btn.click(timeout=1000)
                page.wait_for_timeout(500)
                break
        except Exception:
            pass

    click_expandable_elements(page)
    page.wait_for_timeout(700)

    title = page.title() or url
    body_text = clean_text(page.locator("body").inner_text(timeout=4000))
    body_text = strip_bradford_boilerplate(body_text)

    if len(body_text) < 120:
        raise ValueError("Page text too short after cleaning")

    links = extract_links(page, url)

    return {
        "url": url,
        "title": clean_text(title),
        "service": detect_service_group(url, title, seed_group),
        "seed_group": seed_group,
        "text": body_text[:50000],
        "links": links,
    }


def main():
    os.makedirs("data", exist_ok=True)

    queue = deque()
    queued = set()
    for group, urls in SEED_GROUPS.items():
        for url in urls:
            normalized = normalize_url(url)
            queue.append((normalized, 0, group))
            queued.add(normalized)

    visited = set()
    pages = []

    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        context = browser.new_context(ignore_https_errors=True)
        page = context.new_page()

        while queue and len(visited) < MAX_PAGES:
            url, depth, seed_group = queue.popleft()
            url = normalize_url(url)

            if url in visited:
                continue
            if not is_allowed_url(url):
                continue
            if depth > MAX_DEPTH:
                continue

            visited.add(url)
            print(f"[{len(visited)}/{MAX_PAGES}] depth={depth} {url}")

            try:
                data = scrape_page(page, url, seed_group)
                pages.append(data)

                if depth < MAX_DEPTH:
                    for link in data["links"]:
                        if link not in visited and link not in queued:
                            queue.append((link, depth + 1, seed_group))
                            queued.add(link)
                            

            except PlaywrightTimeoutError:
                print(f"Timeout: {url}")
            except Exception as ex:
                print(f"Failed: {url} -> {ex}")

        browser.close()

    dedup = {}
    for item in pages:
        dedup[item["url"]] = item

    final_pages = list(dedup.values())

    with open(OUTPUT_FILE, "w", encoding="utf-8") as f:
        json.dump(final_pages, f, indent=2, ensure_ascii=False)

    print(f"Saved {len(final_pages)} pages to {OUTPUT_FILE}")


if __name__ == "__main__":
    main()