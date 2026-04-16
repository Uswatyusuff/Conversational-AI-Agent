#!/usr/bin/env python3
"""
evaluate_responses.py
───────────────────────────────────────────────────────────────────────────────
Standalone evaluation script that sends all test cases from test_cases.json
to the live chatbot API and produces a summary report.

Unlike pytest, this script is designed to be run directly and produces a
human-readable pass/fail table plus an output CSV for trend tracking.

Usage:
    python evaluate_responses.py
    python evaluate_responses.py --url http://localhost:8080
    python evaluate_responses.py --output results_2026-04-15.csv
    python evaluate_responses.py --category routing
    python evaluate_responses.py --fail-fast

Output:
    - Coloured table in terminal (via rich)
    - CSV file with all results (append mode by default)
───────────────────────────────────────────────────────────────────────────────
"""

import argparse
import csv
import json
import os
import sys
import time
import uuid
from datetime import datetime
from pathlib import Path

import httpx
from dotenv import load_dotenv

# rich is optional — degrades gracefully to plain output
try:
    from rich.console import Console
    from rich.table import Table
    from rich import print as rprint
    RICH = True
    console = Console()
except ImportError:
    RICH = False
    console = None

load_dotenv()

# ── Configuration ──────────────────────────────────────────────────────────────

DEFAULT_URL = os.getenv("CHATBOT_BASE_URL", "http://localhost:8080")
TEST_CASES_PATH = Path(__file__).parent / "test_cases.json"
OUTPUT_DIR = Path(__file__).parent / "results"

BANNED_PHRASES = [
    "context does not provide",
    "context does not specify",
    "the context does not",
    "based on the context",
    "i am not able to find",
    "i cannot find",
    "as an ai language model",
]


# ── Core evaluation logic ──────────────────────────────────────────────────────

def load_test_cases(path: Path, category: str | None = None) -> list[dict]:
    """Load and optionally filter test cases from JSON."""
    text = path.read_text(encoding="utf-8")
    # Strip JSON comments (lines starting with //)
    cleaned = "\n".join(
        line for line in text.splitlines()
        if not line.strip().startswith("//")
    )
    data = json.loads(cleaned)
    cases = data["test_cases"]
    if category:
        cases = [c for c in cases if c.get("category") == category]
    return cases


def send_message(client: httpx.Client, message: str, session_id: str) -> dict:
    """Send a chat message and return the parsed response."""
    r = client.post("/api/chat", json={"message": message, "sessionId": session_id})
    r.raise_for_status()
    return r.json()


def evaluate_case(case: dict, response: dict, elapsed_ms: int) -> dict:
    """
    Evaluate a single test case against its expected outcomes.
    Returns a result dict with pass/fail details.
    """
    failures = []

    reply = response.get("reply", "")
    service = response.get("service", "")
    suggestions = response.get("suggestions", [])
    reply_lower = reply.lower()

    # Check expected service
    expected_service = case.get("expected_service")
    expected_options = case.get("expected_service_options", [])

    if expected_service and service != expected_service:
        failures.append(f"Expected service '{expected_service}', got '{service}'")

    if expected_options and service not in expected_options:
        failures.append(f"Expected service to be one of {expected_options}, got '{service}'")

    # Check banned phrases in reply
    for phrase in case.get("banned_phrases", []):
        if phrase.lower() in reply_lower:
            failures.append(f"Reply contains banned phrase: '{phrase}'")

    # Check required phrases in reply
    for phrase in case.get("required_phrases", []):
        if phrase.lower() not in reply_lower:
            failures.append(f"Reply missing required phrase: '{phrase}'")

    # Always check for system banned phrases
    for phrase in BANNED_PHRASES:
        if phrase in reply_lower:
            failures.append(f"Reply contains internal leak phrase: '{phrase}'")

    # Check reply length if specified
    min_len = case.get("min_reply_length", 1)
    max_len = case.get("max_reply_length", 9999)
    if len(reply) < min_len:
        failures.append(f"Reply too short: {len(reply)} < {min_len}")
    if len(reply) > max_len:
        failures.append(f"Reply too long: {len(reply)} > {max_len}")

    # Check suggestions are non-empty (where expected)
    if expected_service and expected_service != "Unknown":
        if not suggestions:
            failures.append("No suggestions returned for service query")

    passed = len(failures) == 0

    return {
        "id":           case["id"],
        "category":     case.get("category", ""),
        "input":        case["input"],
        "expected":     expected_service or str(expected_options),
        "got_service":  service,
        "reply_excerpt": reply[:120].replace("\n", " "),
        "passed":       passed,
        "failures":     "; ".join(failures),
        "elapsed_ms":   elapsed_ms,
        "timestamp":    datetime.utcnow().isoformat(),
    }


def run_evaluation(
    base_url: str,
    test_cases: list[dict],
    fail_fast: bool = False,
) -> list[dict]:
    """Run all test cases and return results."""
    results = []

    with httpx.Client(base_url=base_url, timeout=30) as client:
        for i, case in enumerate(test_cases, 1):
            session_id = f"eval-{uuid.uuid4().hex[:10]}"
            message = case["input"]

            print(f"[{i}/{len(test_cases)}] {case['id']}: {message!r}", end=" ... ", flush=True)

            try:
                t0 = time.time()
                response = send_message(client, message, session_id)
                elapsed_ms = int((time.time() - t0) * 1000)

                result = evaluate_case(case, response, elapsed_ms)
                results.append(result)

                status = "✅ PASS" if result["passed"] else f"❌ FAIL: {result['failures']}"
                print(status)

            except Exception as e:
                result = {
                    "id":           case["id"],
                    "category":     case.get("category", ""),
                    "input":        message,
                    "expected":     case.get("expected_service", ""),
                    "got_service":  "ERROR",
                    "reply_excerpt": "",
                    "passed":       False,
                    "failures":     f"Exception: {e}",
                    "elapsed_ms":   0,
                    "timestamp":    datetime.utcnow().isoformat(),
                }
                results.append(result)
                print(f"💥 ERROR: {e}")

            if fail_fast and not result["passed"]:
                print("\n⚠  Stopping early (--fail-fast).")
                break

    return results


def print_summary(results: list[dict]):
    """Print a summary table and overall pass rate."""
    total = len(results)
    passed = sum(1 for r in results if r["passed"])
    failed = total - passed

    print(f"\n{'='*60}")
    print(f"RESULTS: {passed}/{total} passed  ({failed} failed)")
    print(f"{'='*60}")

    if RICH and console:
        table = Table(title="Bradford Chatbot Evaluation", show_lines=True)
        table.add_column("ID",       style="cyan",  no_wrap=True)
        table.add_column("Input",    style="white", max_width=35)
        table.add_column("Expected", style="blue",  max_width=18)
        table.add_column("Got",      style="yellow", max_width=18)
        table.add_column("ms",       style="dim",   no_wrap=True)
        table.add_column("Status",   no_wrap=True)

        for r in results:
            status = "[green]✅ PASS[/green]" if r["passed"] else f"[red]❌ {r['failures'][:40]}[/red]"
            table.add_row(
                r["id"],
                r["input"][:35],
                r["expected"][:18],
                r["got_service"][:18],
                str(r["elapsed_ms"]),
                status,
            )
        console.print(table)

    if failed > 0:
        print("\nFAILED CASES:")
        for r in results:
            if not r["passed"]:
                print(f"  [{r['id']}] {r['input']!r}")
                print(f"      → {r['failures']}")
                print(f"      → Reply: {r['reply_excerpt']!r}")
                print()


def save_csv(results: list[dict], output_path: Path):
    """Save results to CSV for trend tracking."""
    output_path.parent.mkdir(parents=True, exist_ok=True)
    fieldnames = [
        "id", "category", "input", "expected", "got_service",
        "reply_excerpt", "passed", "failures", "elapsed_ms", "timestamp"
    ]
    with open(output_path, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(results)
    print(f"\n📄 Results saved to: {output_path}")


# ── CLI ───────────────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(
        description="Evaluate Bradford Chatbot responses against test cases"
    )
    parser.add_argument(
        "--url", default=DEFAULT_URL,
        help=f"Chatbot API base URL (default: {DEFAULT_URL})"
    )
    parser.add_argument(
        "--output",
        default=str(OUTPUT_DIR / f"eval_{datetime.utcnow().strftime('%Y%m%d_%H%M%S')}.csv"),
        help="Output CSV path"
    )
    parser.add_argument(
        "--category", default=None,
        help="Only run cases from this category (e.g. routing, regression)"
    )
    parser.add_argument(
        "--fail-fast", action="store_true",
        help="Stop on first failure"
    )
    parser.add_argument(
        "--no-save", action="store_true",
        help="Do not save CSV output"
    )
    args = parser.parse_args()

    if not TEST_CASES_PATH.exists():
        print(f"Error: test_cases.json not found at {TEST_CASES_PATH}", file=sys.stderr)
        sys.exit(1)

    test_cases = load_test_cases(TEST_CASES_PATH, args.category)
    print(f"Running {len(test_cases)} test cases against {args.url}")
    print(f"Category filter: {args.category or 'all'}\n")

    results = run_evaluation(args.url, test_cases, fail_fast=args.fail_fast)
    print_summary(results)

    if not args.no_save:
        save_csv(results, Path(args.output))

    failed = sum(1 for r in results if not r["passed"])
    sys.exit(1 if failed > 0 else 0)


if __name__ == "__main__":
    main()
