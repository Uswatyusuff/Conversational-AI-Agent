import csv
import time
import requests
from collections import defaultdict

TARGET_ACCURACY = 85.0
BASE_URL = "http://localhost:5000/api/chat"
SESSION_ID = "automated-test-session"

TEST_CASES = [
    # Council Tax
    {
        "section": "Council Tax",
        "question": "How much council tax do I pay?",
        "expected_service": "Council Tax",
        "must_include_any": ["band", "amount", "council tax", "bill"],
        "bad_phrases": ["context does not specify"],
        "expected_url_keywords": ["council-tax"],
        "expected_suggestion_keywords": ["council tax", "discount", "moved home"],
        "allow_generic": False,
    },
    {
        "section": "Council Tax",
        "question": "How do I pay my council tax?",
        "expected_service": "Council Tax",
        "must_include_any": ["pay", "online", "bill", "direct debit"],
        "bad_phrases": ["context does not specify"],
        "expected_url_keywords": ["council-tax", "pay"],
        "expected_suggestion_keywords": ["council tax", "discount", "moved home"],
        "allow_generic": False,
    },
    {
        "section": "Council Tax",
        "question": "Can I get a council tax discount?",
        "expected_service": "Council Tax",
        "must_include_any": ["discount", "reduction", "apply", "eligible"],
        "bad_phrases": ["context does not specify"],
        "expected_url_keywords": ["council-tax", "discount", "reduce"],
        "expected_suggestion_keywords": ["council tax", "discount", "moved home"],
        "allow_generic": False,
    },

    # Waste & Bins
    {
        "section": "Waste & Bins",
        "question": "When is my bin collection?",
        "expected_service": "Waste & Bins",
        "must_include_any": ["postcode", "collection", "bin"],
        "bad_phrases": ["context does not specify"],
        "expected_url_keywords": ["bins", "waste", "recycling"],
        "expected_suggestion_keywords": ["bin", "missed", "postcode", "address"],
        "allow_generic": False,
    },
    {
        "section": "Waste & Bins",
        "question": "Report a missed bin",
        "expected_service": "Waste & Bins",
        "must_include_any": ["missed bin", "report", "form", "call"],
        "bad_phrases": ["context does not specify"],
        "expected_url_keywords": ["missed-bin", "bins", "waste"],
        "expected_suggestion_keywords": ["bin", "missed", "postcode", "address"],
        "allow_generic": False,
    },
    {
        "section": "Waste & Bins",
        "question": "How much is a new bin?",
        "expected_service": "Waste & Bins",
        "must_include_any": ["£", "cost", "new bin", "charge"],
        "bad_phrases": ["context does not specify"],
        "expected_url_keywords": ["bins", "containers", "waste"],
        "expected_suggestion_keywords": ["bin", "missed", "postcode", "address"],
        "allow_generic": False,
    },

    # Benefits & Support
    {
        "section": "Benefits & Support",
        "question": "How much benefits can I get?",
        "expected_service": "Benefits & Support",
        "must_include_any": ["depends", "eligibility", "income", "circumstances", "universal credit", "housing benefit"],
        "bad_phrases": [
            "context does not specify exact amounts",
            "context does not specify",
            "not clearly specify",
        ],
        "expected_url_keywords": ["benefits", "welfare"],
        "expected_suggestion_keywords": ["apply", "eligible", "evidence"],
        "allow_generic": False,
    },
    {
        "section": "Benefits & Support",
        "question": "How do I apply for benefits?",
        "expected_service": "Benefits & Support",
        "must_include_any": ["apply", "universal credit", "housing benefit", "visit", "contact"],
        "bad_phrases": ["context does not clearly specify"],
        "expected_url_keywords": ["benefits"],
        "expected_suggestion_keywords": ["apply", "eligible", "evidence"],
        "allow_generic": False,
    },
    {
        "section": "Benefits & Support",
        "question": "I need help paying bills",
        "expected_service": "Benefits & Support",
        "must_include_any": ["support", "advice", "help", "benefit", "debt", "bills"],
        "bad_phrases": ["context does not specify"],
        "expected_url_keywords": ["benefits", "money-advice", "welfare"],
        "expected_suggestion_keywords": ["apply", "eligible", "evidence"],
        "allow_generic": False,
    },

    # Education
    {
        "section": "Education",
        "question": "How do I apply for a school place?",
        "expected_service": "Education",
        "must_include_any": ["apply", "school place", "admissions", "email", "form"],
        "bad_phrases": ["context does not specify"],
        "expected_url_keywords": ["education", "school-admissions"],
        "expected_suggestion_keywords": ["school place", "deadline", "transfer"],
        "allow_generic": False,
    },
    {
        "section": "Education",
        "question": "How do in-year transfers work?",
        "expected_service": "Education",
        "must_include_any": ["in-year", "transfer", "term", "school"],
        "bad_phrases": ["context does not specify"],
        "expected_url_keywords": ["education", "school-admissions", "in-year"],
        "expected_suggestion_keywords": ["school place", "deadline", "transfer"],
        "allow_generic": False,
    },
    {
        "section": "Education",
        "question": "How do I track my application?",
        "expected_service": "Education",
        "must_include_any": ["track", "application", "account", "offer", "online"],
        "bad_phrases": ["context does not specify"],
        "expected_url_keywords": ["education", "school-admissions"],
        "expected_suggestion_keywords": ["school place", "deadline", "transfer"],
        "allow_generic": False,
    },

    # Planning
    {
        "section": "Planning",
        "question": "How can I check my planning application status?",
        "expected_service": "Planning",
        "must_include_any": ["planning", "application", "status", "online", "track"],
        "bad_phrases": ["context does not specify"],
        "expected_url_keywords": ["planning"],
        "expected_suggestion_keywords": ["planning", "application", "permission"],
        "allow_generic": False,
    },
    {
        "section": "Planning",
        "question": "How do I apply for planning permission?",
        "expected_service": "Planning",
        "must_include_any": ["apply", "planning permission", "online", "application"],
        "bad_phrases": ["context does not specify"],
        "expected_url_keywords": ["planning"],
        "expected_suggestion_keywords": ["planning", "application", "permission"],
        "allow_generic": False,
    },

    # Libraries
    {
        "section": "Libraries",
        "question": "How do I renew library books online?",
        "expected_service": "Libraries",
        "must_include_any": ["renew", "online", "library account", "log in"],
        "bad_phrases": ["context does not specify"],
        "expected_url_keywords": ["libraries"],
        "expected_suggestion_keywords": ["library", "e-books", "join"],
        "allow_generic": False,
    },
    {
        "section": "Libraries",
        "question": "Can I borrow e-books?",
        "expected_service": "Libraries",
        "must_include_any": ["e-books", "borrow", "library card", "digital"],
        "bad_phrases": ["context does not specify"],
        "expected_url_keywords": ["libraries"],
        "expected_suggestion_keywords": ["library", "e-books", "join"],
        "allow_generic": False,
    },

    # Housing
    {
        "section": "Housing",
        "question": "I am homeless, what should I do?",
        "expected_service": "Housing",
        "must_include_any": ["homeless", "housing", "advice", "contact", "help"],
        "bad_phrases": ["context does not specify"],
        "expected_url_keywords": ["housing", "homeless"],
        "expected_suggestion_keywords": ["housing", "homeless", "home"],
        "allow_generic": False,
    },
    {
        "section": "Housing",
        "question": "How do I apply for council housing?",
        "expected_service": "Housing",
        "must_include_any": ["apply", "housing", "contact", "service"],
        "bad_phrases": ["context does not specify"],
        "expected_url_keywords": ["housing"],
        "expected_suggestion_keywords": ["housing", "homeless", "home"],
        "allow_generic": False,
    },
    {
        "section": "Housing",
        "question": "Can I get temporary accommodation?",
        "expected_service": "Housing",
        "must_include_any": ["temporary accommodation", "housing", "eligible", "support"],
        "bad_phrases": ["context does not specify"],
        "expected_url_keywords": ["housing"],
        "expected_suggestion_keywords": ["housing", "homeless", "home"],
        "allow_generic": False,
    },

    # Contact Us
    {
        "section": "Contact Us",
        "question": "How can I contact the council?",
        "expected_service": "Contact Us",
        "must_include_any": ["contact", "council", "phone", "email", "social"],
        "bad_phrases": ["context does not provide"],
        "expected_url_keywords": ["contact"],
        "expected_suggestion_keywords": ["contact", "phone", "email", "alerts"],
        "allow_generic": True,
    },
    {
        "section": "Contact Us",
        "question": "How do I sign up for email alerts?",
        "expected_service": "Contact Us",
        "must_include_any": ["email alerts", "email address", "sign up", "subscribe"],
        "bad_phrases": ["context does not provide"],
        "expected_url_keywords": ["email-alerts", "contact"],
        "expected_suggestion_keywords": ["contact", "phone", "alerts"],
        "allow_generic": False,
    },
]

GENERIC_BAD_PHRASES = [
    "context does not specify",
    "context does not clearly specify",
    "context does not provide",
    "please refer to the official",
    "for more details",
    "for detailed information",
    "refer to the official",
    "not clearly listed",
    "not clearly stated",
]

SECTION_KEYWORDS = {
    "Council Tax": ["council tax", "discount", "reduction", "bill", "band"],
    "Waste & Bins": ["bin", "bins", "waste", "recycling", "postcode", "collection"],
    "Benefits & Support": ["benefit", "support", "universal credit", "housing benefit", "blue badge"],
    "Education": ["school", "admissions", "education", "transfer", "application"],
    "Planning": ["planning", "permission", "application", "development", "building control"],
    "Libraries": ["library", "libraries", "books", "e-books", "renew"],
    "Housing": ["housing", "homeless", "rent", "accommodation", "eviction"],
    "Contact Us": ["contact", "phone", "email", "alerts", "complaint"],
}


def call_chat_api(question: str, session_id: str) -> dict:
    payload = {
        "message": question,
        "sessionId": session_id
    }
    response = requests.post(BASE_URL, json=payload, timeout=90)
    response.raise_for_status()
    return response.json()


def normalize_suggestions(value):
    if value is None:
        return []
    if isinstance(value, list):
        return [str(v) for v in value]
    return [str(value)]


def text_contains_any(text: str, phrases: list[str]) -> bool:
    text_lower = (text or "").lower()
    return any(phrase.lower() in text_lower for phrase in phrases)


def grade_service(actual: str, expected: str) -> tuple[str, str]:
    actual_clean = (actual or "").strip().lower()
    expected_clean = (expected or "").strip().lower()

    if actual_clean == expected_clean:
        return "PASS", "Service matched expected section."
    return "FAIL", f"Expected service '{expected}', got '{actual}'."


def grade_response(reply: str, test: dict) -> tuple[str, str]:
    reply_lower = (reply or "").strip().lower()

    if not reply_lower:
        return "FAIL", "Empty response."

    bad_phrases = [p.lower() for p in test.get("bad_phrases", [])]
    must_include_any = [p.lower() for p in test.get("must_include_any", [])]
    allow_generic = test.get("allow_generic", True)

    if not allow_generic:
        for phrase in bad_phrases + GENERIC_BAD_PHRASES:
            if phrase in reply_lower:
                return "FAIL", f"Generic fallback phrase detected: '{phrase}'."

    if must_include_any and not any(term in reply_lower for term in must_include_any):
        return "FAIL", f"Response missing useful expected concepts: {must_include_any}"

    if len(reply_lower.split()) < 5:
        return "FAIL", "Response too short to be useful."

    return "PASS", "Response appears relevant and informative."


def grade_url(next_steps_url: str, test: dict) -> tuple[str, str]:
    expected_url_keywords = [k.lower() for k in test.get("expected_url_keywords", [])]

    if not next_steps_url:
        return "FAIL", "Missing next steps URL."

    url_lower = next_steps_url.lower()
    if expected_url_keywords and not any(k in url_lower for k in expected_url_keywords):
        return "FAIL", f"URL does not appear relevant. Expected keywords: {expected_url_keywords}"

    return "PASS", "URL appears relevant."


def grade_suggestions(suggestions: list[str], test: dict) -> tuple[str, str]:
    expected_keywords = [k.lower() for k in test.get("expected_suggestion_keywords", [])]

    if not suggestions:
        return "FAIL", "Missing suggestions."

    combined = " | ".join(suggestions).lower()

    if expected_keywords and not any(k in combined for k in expected_keywords):
        return "FAIL", f"Suggestions do not appear relevant. Expected keywords: {expected_keywords}"

    return "PASS", "Suggestions appear relevant."


def final_grade(service_result: str, response_result: str, url_result: str, suggestion_result: str) -> tuple[str, str]:
    required = [service_result, response_result]
    supporting = [url_result, suggestion_result]

    if all(r == "PASS" for r in required) and sum(1 for r in supporting if r == "PASS") >= 1:
        return "PASS", "Routing and response quality passed."
    if service_result == "FAIL":
        return "FAIL", "Failed routing."
    if response_result == "FAIL":
        return "FAIL", "Failed response quality."
    return "FAIL", "Supporting quality checks failed."


def summarise_results(results):
    total = len(results)

    routing_passed = sum(1 for r in results if r["service_result"] == "PASS")
    response_passed = sum(1 for r in results if r["response_result"] == "PASS")
    url_passed = sum(1 for r in results if r["url_result"] == "PASS")
    suggestions_passed = sum(1 for r in results if r["suggestions_result"] == "PASS")
    final_passed = sum(1 for r in results if r["final_result"] == "PASS")

    routing_accuracy = (routing_passed / total * 100) if total else 0.0
    response_accuracy = (response_passed / total * 100) if total else 0.0
    url_accuracy = (url_passed / total * 100) if total else 0.0
    suggestions_accuracy = (suggestions_passed / total * 100) if total else 0.0
    final_accuracy = (final_passed / total * 100) if total else 0.0

    section_stats = defaultdict(lambda: {
        "total": 0,
        "routing_passed": 0,
        "response_passed": 0,
        "final_passed": 0
    })

    for r in results:
        section = r.get("section", "Uncategorised")
        section_stats[section]["total"] += 1
        if r["service_result"] == "PASS":
            section_stats[section]["routing_passed"] += 1
        if r["response_result"] == "PASS":
            section_stats[section]["response_passed"] += 1
        if r["final_result"] == "PASS":
            section_stats[section]["final_passed"] += 1

    print("\n=== CHATBOT TEST SUMMARY ===")
    print(f"Total tests: {total}")
    print(f"Routing passed: {routing_passed}")
    print(f"Response quality passed: {response_passed}")
    print(f"URL relevance passed: {url_passed}")
    print(f"Suggestion relevance passed: {suggestions_passed}")
    print(f"Final passed: {final_passed}")

    print(f"\nRouting accuracy: {routing_accuracy:.2f}%")
    print(f"Response quality accuracy: {response_accuracy:.2f}%")
    print(f"URL relevance accuracy: {url_accuracy:.2f}%")
    print(f"Suggestion relevance accuracy: {suggestions_accuracy:.2f}%")
    print(f"Final accuracy: {final_accuracy:.2f}%")

    if final_accuracy >= TARGET_ACCURACY:
        print(f"✅ Target met ({TARGET_ACCURACY:.0f}%)")
    else:
        print(f"❌ Target not met ({TARGET_ACCURACY:.0f}%)")

    print("\n=== PER-SECTION BREAKDOWN ===")
    for section, stats in section_stats.items():
        total_section = stats["total"]
        routing_acc = (stats["routing_passed"] / total_section * 100) if total_section else 0.0
        response_acc = (stats["response_passed"] / total_section * 100) if total_section else 0.0
        final_acc = (stats["final_passed"] / total_section * 100) if total_section else 0.0
        print(
            f"{section}: "
            f"routing {stats['routing_passed']}/{total_section} ({routing_acc:.2f}%), "
            f"response {stats['response_passed']}/{total_section} ({response_acc:.2f}%), "
            f"final {stats['final_passed']}/{total_section} ({final_acc:.2f}%)"
        )

    return {
        "total": total,
        "routing_passed": routing_passed,
        "response_passed": response_passed,
        "url_passed": url_passed,
        "suggestions_passed": suggestions_passed,
        "final_passed": final_passed,
        "routing_accuracy": routing_accuracy,
        "response_accuracy": response_accuracy,
        "url_accuracy": url_accuracy,
        "suggestions_accuracy": suggestions_accuracy,
        "final_accuracy": final_accuracy,
        "target_met": final_accuracy >= TARGET_ACCURACY,
        "sections": {
            section: {
                "total": stats["total"],
                "routing_passed": stats["routing_passed"],
                "response_passed": stats["response_passed"],
                "final_passed": stats["final_passed"],
                "routing_accuracy": (stats["routing_passed"] / stats["total"] * 100) if stats["total"] else 0.0,
                "response_accuracy": (stats["response_passed"] / stats["total"] * 100) if stats["total"] else 0.0,
                "final_accuracy": (stats["final_passed"] / stats["total"] * 100) if stats["total"] else 0.0,
            }
            for section, stats in section_stats.items()
        }
    }


def write_summary_csv(summary, path="test_summary.csv"):
    with open(path, "w", newline="", encoding="utf-8") as f:
        writer = csv.writer(f)

        writer.writerow(["Metric", "Value"])
        writer.writerow(["Total tests", summary["total"]])
        writer.writerow(["Routing passed", summary["routing_passed"]])
        writer.writerow(["Response quality passed", summary["response_passed"]])
        writer.writerow(["URL relevance passed", summary["url_passed"]])
        writer.writerow(["Suggestion relevance passed", summary["suggestions_passed"]])
        writer.writerow(["Final passed", summary["final_passed"]])
        writer.writerow(["Routing accuracy %", f"{summary['routing_accuracy']:.2f}"])
        writer.writerow(["Response quality accuracy %", f"{summary['response_accuracy']:.2f}"])
        writer.writerow(["URL relevance accuracy %", f"{summary['url_accuracy']:.2f}"])
        writer.writerow(["Suggestion relevance accuracy %", f"{summary['suggestions_accuracy']:.2f}"])
        writer.writerow(["Final accuracy %", f"{summary['final_accuracy']:.2f}"])
        writer.writerow(["Target met", "Yes" if summary["target_met"] else "No"])

        writer.writerow([])
        writer.writerow([
            "Section",
            "Routing Passed",
            "Response Passed",
            "Final Passed",
            "Total",
            "Routing Accuracy %",
            "Response Accuracy %",
            "Final Accuracy %"
        ])

        for section, stats in summary["sections"].items():
            writer.writerow([
                section,
                stats["routing_passed"],
                stats["response_passed"],
                stats["final_passed"],
                stats["total"],
                f"{stats['routing_accuracy']:.2f}",
                f"{stats['response_accuracy']:.2f}",
                f"{stats['final_accuracy']:.2f}",
            ])


def main():
    results = []

    print(f"Running {len(TEST_CASES)} tests against {BASE_URL}\n")

    for i, test in enumerate(TEST_CASES, start=1):
        question = test["question"]
        expected_service = test["expected_service"]

        try:
            data = call_chat_api(question, SESSION_ID)

            actual_service = data.get("service", "")
            reply = data.get("reply", "") or data.get("answer", "")
            next_steps_url = data.get("nextStepsUrl", "")
            suggestions = normalize_suggestions(data.get("suggestions"))

            service_result, service_reason = grade_service(actual_service, expected_service)
            response_result, response_reason = grade_response(reply, test)
            url_result, url_reason = grade_url(next_steps_url, test)
            suggestions_result, suggestions_reason = grade_suggestions(suggestions, test)
            final_result, final_reason = final_grade(
                service_result,
                response_result,
                url_result,
                suggestions_result
            )

            row = {
                "test_no": i,
                "section": test["section"],
                "question": question,
                "expected_service": expected_service,
                "actual_service": actual_service,
                "service_result": service_result,
                "service_reason": service_reason,
                "response_result": response_result,
                "response_reason": response_reason,
                "url_result": url_result,
                "url_reason": url_reason,
                "suggestions_result": suggestions_result,
                "suggestions_reason": suggestions_reason,
                "final_result": final_result,
                "final_reason": final_reason,
                "reply": reply,
                "next_steps_url": next_steps_url,
                "suggestions": " | ".join(suggestions),
            }

            print(
                f"[{i}/{len(TEST_CASES)}] "
                f"FINAL={final_result} | "
                f"SERVICE={service_result} | "
                f"RESPONSE={response_result} | "
                f"{question} -> {actual_service}"
            )

        except Exception as ex:
            row = {
                "test_no": i,
                "section": test["section"],
                "question": question,
                "expected_service": expected_service,
                "actual_service": "ERROR",
                "service_result": "ERROR",
                "service_reason": str(ex),
                "response_result": "ERROR",
                "response_reason": str(ex),
                "url_result": "ERROR",
                "url_reason": str(ex),
                "suggestions_result": "ERROR",
                "suggestions_reason": str(ex),
                "final_result": "ERROR",
                "final_reason": str(ex),
                "reply": str(ex),
                "next_steps_url": "",
                "suggestions": "",
            }

            print(f"[{i}/{len(TEST_CASES)}] ERROR | {question} -> {ex}")

        results.append(row)
        time.sleep(0.4)

    output_file = "chatbot_test_results.csv"
    with open(output_file, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(
            f,
            fieldnames=[
                "test_no",
                "section",
                "question",
                "expected_service",
                "actual_service",
                "service_result",
                "service_reason",
                "response_result",
                "response_reason",
                "url_result",
                "url_reason",
                "suggestions_result",
                "suggestions_reason",
                "final_result",
                "final_reason",
                "reply",
                "next_steps_url",
                "suggestions",
            ],
        )
        writer.writeheader()
        writer.writerows(results)

    print(f"\nSaved detailed results to {output_file}")

    summary = summarise_results(results)
    write_summary_csv(summary)
    print("\nSaved summary to test_summary.csv")


if __name__ == "__main__":
    main()