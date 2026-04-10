import csv
import json
import time
import requests

BASE_URL = "http://localhost:5000/api/chat"
SESSION_ID = "automated-test-session"

TEST_CASES = [
    # Council Tax
    {"section": "Council Tax", "question": "How much council tax do I pay?", "expected_service": "Council Tax"},
    {"section": "Council Tax", "question": "How do I pay my council tax?", "expected_service": "Council Tax"},
    {"section": "Council Tax", "question": "Can I get a council tax discount?", "expected_service": "Council Tax"},
    {"section": "Council Tax", "question": "What happens if I don’t pay council tax?", "expected_service": "Council Tax"},
    {"section": "Council Tax", "question": "How do I check my council tax balance?", "expected_service": "Council Tax"},
    {"section": "Council Tax", "question": "Can I pay council tax monthly?", "expected_service": "Council Tax"},
    {"section": "Council Tax", "question": "What are council tax bands?", "expected_service": "Council Tax"},
    {"section": "Council Tax", "question": "I moved house, what happens to my council tax?", "expected_service": "Council Tax"},
    {"section": "Council Tax", "question": "Can students get council tax discount?", "expected_service": "Council Tax"},
    {"section": "Council Tax", "question": "How do I set up direct debit?", "expected_service": "Council Tax"},
    {"section": "Council Tax", "question": "Can I get council tax reduction?", "expected_service": "Council Tax"},
    {"section": "Council Tax", "question": "What is band D council tax?", "expected_service": "Council Tax"},
    {"section": "Council Tax", "question": "I cannot afford council tax, what can I do?", "expected_service": "Council Tax"},
    {"section": "Council Tax", "question": "How do I contact council tax team?", "expected_service": "Council Tax"},
    {"section": "Council Tax", "question": "What happens if I miss a payment?", "expected_service": "Council Tax"},
    {"section": "Council Tax", "question": "Can I appeal my council tax band?", "expected_service": "Council Tax"},
    {"section": "Council Tax", "question": "Do I pay council tax if I live alone?", "expected_service": "Council Tax"},
    {"section": "Council Tax", "question": "What discounts are available?", "expected_service": "Council Tax"},
    {"section": "Council Tax", "question": "How do I update my council tax details?", "expected_service": "Council Tax"},
    {"section": "Council Tax", "question": "Can I pay council tax online?", "expected_service": "Council Tax"},
    
    # Waste & Bins
    {"section": "Waste & Bins", "question": "When is my bin collection?", "expected_service": "Waste & Bins"},
    {"section": "Waste & Bins", "question": "What day is my bin collected?", "expected_service": "Waste & Bins"},
    {"section": "Waste & Bins", "question": "My bin was not collected", "expected_service": "Waste & Bins"},
    {"section": "Waste & Bins", "question": "Report a missed bin", "expected_service": "Waste & Bins"},
    {"section": "Waste & Bins", "question": "I need a new bin", "expected_service": "Waste & Bins"},
    {"section": "Waste & Bins", "question": "How much is a new bin?", "expected_service": "Waste & Bins"},
    {"section": "Waste & Bins", "question": "What goes in recycling bin?", "expected_service": "Waste & Bins"},
    {"section": "Waste & Bins", "question": "What goes in general waste?", "expected_service": "Waste & Bins"},
    {"section": "Waste & Bins", "question": "Tell me about garden waste", "expected_service": "Waste & Bins"},
    {"section": "Waste & Bins", "question": "Do you collect bins on Sundays?", "expected_service": "Waste & Bins"},
    {"section": "Waste & Bins", "question": "Can I change my bin collection day?", "expected_service": "Waste & Bins"},
    {"section": "Waste & Bins", "question": "What time should I put my bin out?", "expected_service": "Waste & Bins"},
    {"section": "Waste & Bins", "question": "How do I get a replacement bin?", "expected_service": "Waste & Bins"},
    {"section": "Waste & Bins", "question": "What if my bin is damaged?", "expected_service": "Waste & Bins"},
    {"section": "Waste & Bins", "question": "How do I report missed recycling?", "expected_service": "Waste & Bins"},
    {"section": "Waste & Bins", "question": "Do you collect bulky waste?", "expected_service": "Waste & Bins"},
    {"section": "Waste & Bins", "question": "How do I book bulky waste collection?", "expected_service": "Waste & Bins"},
    {"section": "Waste & Bins", "question": "What bins do I need?", "expected_service": "Waste & Bins"},
    {"section": "Waste & Bins", "question": "How do I recycle electronics?", "expected_service": "Waste & Bins"},
    {"section": "Waste & Bins", "question": "What happens if I miss collection day?", "expected_service": "Waste & Bins"},

    # Benefits & Support
    {"section": "Benefits & Support", "question": "Can I get benefits?", "expected_service": "Benefits & Support"},
    {"section": "Benefits & Support", "question": "How do I apply for benefits?", "expected_service": "Benefits & Support"},
    {"section": "Benefits & Support", "question": "Am I eligible for benefits?", "expected_service": "Benefits & Support"},
    {"section": "Benefits & Support", "question": "How much benefits can I get?", "expected_service": "Benefits & Support"},
    {"section": "Benefits & Support", "question": "What documents do I need for benefits?", "expected_service": "Benefits & Support"},
    {"section": "Benefits & Support", "question": "How do I apply for a Blue Badge?", "expected_service": "Benefits & Support"},
    {"section": "Benefits & Support", "question": "Am I eligible for a Blue Badge?", "expected_service": "Benefits & Support"},
    {"section": "Benefits & Support", "question": "What evidence do I need for Blue Badge?", "expected_service": "Benefits & Support"},
    {"section": "Benefits & Support", "question": "Can I get support if I’m unemployed?", "expected_service": "Benefits & Support"},
    {"section": "Benefits & Support", "question": "What financial support is available?", "expected_service": "Benefits & Support"},
    {"section": "Benefits & Support", "question": "How do I apply for housing benefit?", "expected_service": "Benefits & Support"},
    {"section": "Benefits & Support", "question": "What is universal credit?", "expected_service": "Benefits & Support"},
    {"section": "Benefits & Support", "question": "Can I get hardship support?", "expected_service": "Benefits & Support"},
    {"section": "Benefits & Support", "question": "I need help paying bills", "expected_service": "Benefits & Support"},
    {"section": "Benefits & Support", "question": "How do I apply for council tax support?", "expected_service": "Benefits & Support"},
    {"section": "Benefits & Support", "question": "What help is available for disabled people?", "expected_service": "Benefits & Support"},
    {"section": "Benefits & Support", "question": "Can I get mobility support?", "expected_service": "Benefits & Support"},
    {"section": "Benefits & Support", "question": "What benefits can I claim?", "expected_service": "Benefits & Support"},
    {"section": "Benefits & Support", "question": "How long does application take?", "expected_service": "Benefits & Support"},
    {"section": "Benefits & Support", "question": "Who can help me apply?", "expected_service": "Benefits & Support"},
    
    # Education
   {"section": "Education", "question": "How do I apply for a school place?", "expected_service": "Education"},
    {"section": "Education", "question": "What is the deadline for school admissions?", "expected_service": "Education"},
    {"section": "Education", "question": "How do in-year transfers work?", "expected_service": "Education"},
    {"section": "Education", "question": "Can I change my child’s school?", "expected_service": "Education"},
    {"section": "Education", "question": "How do I apply for primary school?", "expected_service": "Education"},
    {"section": "Education", "question": "How do I apply for secondary school?", "expected_service": "Education"},
    {"section": "Education", "question": "Can I appeal a school decision?", "expected_service": "Education"},
    {"section": "Education", "question": "What documents are needed for school admission?", "expected_service": "Education"},
    {"section": "Education", "question": "How do I contact admissions team?", "expected_service": "Education"},
    {"section": "Education", "question": "When do school applications open?", "expected_service": "Education"},
    {"section": "Education", "question": "How do I apply late?", "expected_service": "Education"},
    {"section": "Education", "question": "What happens after applying?", "expected_service": "Education"},
    {"section": "Education", "question": "Can I choose multiple schools?", "expected_service": "Education"},
    {"section": "Education", "question": "How are school places allocated?", "expected_service": "Education"},
    {"section": "Education", "question": "What if I miss the deadline?", "expected_service": "Education"},
    {"section": "Education", "question": "Do I need proof of address?", "expected_service": "Education"},
    {"section": "Education", "question": "How do I apply for school transport?", "expected_service": "Education"},
    {"section": "Education", "question": "Can I transfer mid-year?", "expected_service": "Education"},
    {"section": "Education", "question": "What is EHCP?", "expected_service": "Education"},
    {"section": "Education", "question": "How do I track my application?", "expected_service": "Education"},

    # Planning
    {"section": "Planning", "question": "How can I check my planning application status?", "expected_service": "Planning"},
    {"section": "Planning", "question": "View planning applications", "expected_service": "Planning"},
    {"section": "Planning", "question": "How do I apply for planning permission?", "expected_service": "Planning"},
    {"section": "Planning", "question": "Can I object to a planning application?", "expected_service": "Planning"},
    {"section": "Planning", "question": "How do I comment on a planning application?", "expected_service": "Planning"},
    {"section": "Planning", "question": "Do I need planning permission?", "expected_service": "Planning"},
    {"section": "Planning", "question": "What is planning permission?", "expected_service": "Planning"},
    {"section": "Planning", "question": "How long does a planning application take?", "expected_service": "Planning"},
    {"section": "Planning", "question": "Can I track my planning application online?", "expected_service": "Planning"},
    {"section": "Planning", "question": "What documents do I need for a planning application?", "expected_service": "Planning"},
    {"section": "Planning", "question": "How do I submit a planning application?", "expected_service": "Planning"},
    {"section": "Planning", "question": "Can I appeal a planning decision?", "expected_service": "Planning"},
    {"section": "Planning", "question": "What is building control?", "expected_service": "Planning"},
    {"section": "Planning", "question": "How do I contact the planning team?", "expected_service": "Planning"},
    {"section": "Planning", "question": "Are there fees for planning permission?", "expected_service": "Planning"},
    {"section": "Planning", "question": "Can I extend my house without permission?", "expected_service": "Planning"},
    {"section": "Planning", "question": "What is permitted development?", "expected_service": "Planning"},
    {"section": "Planning", "question": "Can neighbours comment on my planning application?", "expected_service": "Planning"},
    {"section": "Planning", "question": "How do I view historical planning applications?", "expected_service": "Planning"},
    {"section": "Planning", "question": "What happens after planning permission is approved?", "expected_service": "Planning"},
    # Libraries
    {"section": "Libraries", "question": "How do I renew library books online?", "expected_service": "Libraries"},
    {"section": "Libraries", "question": "Can I borrow e-books?", "expected_service": "Libraries"},
    {"section": "Libraries", "question": "How do I join the library?", "expected_service": "Libraries"},
    {"section": "Libraries", "question": "How many books can I borrow?", "expected_service": "Libraries"},
    {"section": "Libraries", "question": "Can I reserve a book?", "expected_service": "Libraries"},
    {"section": "Libraries", "question": "What is the borrowing limit?", "expected_service": "Libraries"},
    {"section": "Libraries", "question": "How do I get a library card?", "expected_service": "Libraries"},
    {"section": "Libraries", "question": "Can I use the digital library?", "expected_service": "Libraries"},
    {"section": "Libraries", "question": "How do I access audiobooks?", "expected_service": "Libraries"},
    {"section": "Libraries", "question": "Can I renew overdue books?", "expected_service": "Libraries"},
    {"section": "Libraries", "question": "What happens if I don’t return library books?", "expected_service": "Libraries"},
    {"section": "Libraries", "question": "Are there fines for overdue books?", "expected_service": "Libraries"},
    {"section": "Libraries", "question": "How do I reset my library account password?", "expected_service": "Libraries"},
    {"section": "Libraries", "question": "Where is my nearest library?", "expected_service": "Libraries"},
    {"section": "Libraries", "question": "What are library opening hours?", "expected_service": "Libraries"},
    {"section": "Libraries", "question": "Can I print at the library?", "expected_service": "Libraries"},
    {"section": "Libraries", "question": "Do libraries have study spaces?", "expected_service": "Libraries"},
    {"section": "Libraries", "question": "Can children join the library?", "expected_service": "Libraries"},
    {"section": "Libraries", "question": "How do I borrow digital books?", "expected_service": "Libraries"},
    {"section": "Libraries", "question": "Can I borrow magazines online?", "expected_service": "Libraries"},

    # Housing
    {"section": "Housing", "question": "How do I get housing support?", "expected_service": "Housing"},
    {"section": "Housing", "question": "I am homeless, what should I do?", "expected_service": "Housing"},
    {"section": "Housing", "question": "Can I get help with rent?", "expected_service": "Housing"},
    {"section": "Housing", "question": "How do I apply for council housing?", "expected_service": "Housing"},
    {"section": "Housing", "question": "I am at risk of eviction", "expected_service": "Housing"},
    {"section": "Housing", "question": "What support is available for tenants?", "expected_service": "Housing"},
    {"section": "Housing", "question": "How do I find a home?", "expected_service": "Housing"},
    {"section": "Housing", "question": "Can I apply for emergency housing?", "expected_service": "Housing"},
    {"section": "Housing", "question": "What documents do I need for housing support?", "expected_service": "Housing"},
    {"section": "Housing", "question": "How long does a housing application take?", "expected_service": "Housing"},
    {"section": "Housing", "question": "Can I get temporary accommodation?", "expected_service": "Housing"},
    {"section": "Housing", "question": "Who do I contact for housing help?", "expected_service": "Housing"},
    {"section": "Housing", "question": "What is housing benefit?", "expected_service": "Housing"},
    {"section": "Housing", "question": "Can I get rent support?", "expected_service": "Housing"},
    {"section": "Housing", "question": "What happens if I lose my home?", "expected_service": "Housing"},
    {"section": "Housing", "question": "How do I report landlord issues?", "expected_service": "Housing"},
    {"section": "Housing", "question": "Can I transfer my housing?", "expected_service": "Housing"},
    {"section": "Housing", "question": "What is priority housing?", "expected_service": "Housing"},
    {"section": "Housing", "question": "Can families get housing help?", "expected_service": "Housing"},
    {"section": "Housing", "question": "What is supported housing?", "expected_service": "Housing"},

    # Contact Us
    {"section": "Contact Us", "question": "How can I contact the council?", "expected_service": "Contact Us"},
    {"section": "Contact Us", "question": "What is the council phone number?", "expected_service": "Contact Us"},
    {"section": "Contact Us", "question": "Can I email the council?", "expected_service": "Contact Us"},
    {"section": "Contact Us", "question": "What are the council opening hours?", "expected_service": "Contact Us"},
    {"section": "Contact Us", "question": "Where is the council office?", "expected_service": "Contact Us"},
    {"section": "Contact Us", "question": "How do I make a complaint?", "expected_service": "Contact Us"},
    {"section": "Contact Us", "question": "How do I give feedback to the council?", "expected_service": "Contact Us"},
    {"section": "Contact Us", "question": "Can I visit the council in person?", "expected_service": "Contact Us"},
    {"section": "Contact Us", "question": "What is the customer service number?", "expected_service": "Contact Us"},
    {"section": "Contact Us", "question": "How do I contact support?", "expected_service": "Contact Us"},
    {"section": "Contact Us", "question": "Is there an online chat available?", "expected_service": "Contact Us"},
    {"section": "Contact Us", "question": "How do I book an appointment with the council?", "expected_service": "Contact Us"},
    {"section": "Contact Us", "question": "What departments can I contact?", "expected_service": "Contact Us"},
    {"section": "Contact Us", "question": "Is there an emergency contact number?", "expected_service": "Contact Us"},
    {"section": "Contact Us", "question": "Can I contact the council via social media?", "expected_service": "Contact Us"},
    {"section": "Contact Us", "question": "How do I get updates from the council?", "expected_service": "Contact Us"},
    {"section": "Contact Us", "question": "How do I sign up for email alerts?", "expected_service": "Contact Us"},
    {"section": "Contact Us", "question": "Can I contact the council by post?", "expected_service": "Contact Us"},
    {"section": "Contact Us", "question": "Where do I send documents to the council?", "expected_service": "Contact Us"},
    {"section": "Contact Us", "question": "Who handles complaints in the council?", "expected_service": "Contact Us"},
    ]


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
        return value
    return [str(value)]


def grade_service(actual: str, expected: str) -> str:
    if (actual or "").strip().lower() == (expected or "").strip().lower():
        return "PASS"
    return "FAIL"


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

            service_result = grade_service(actual_service, expected_service)

            row = {
                "test_no": i,
                "section": test["section"],
                "question": question,
                "expected_service": expected_service,
                "actual_service": actual_service,
                "service_result": service_result,
                "reply": reply,
                "next_steps_url": next_steps_url,
                "suggestions": " | ".join(suggestions),
            }

            print(f"[{i}/{len(TEST_CASES)}] {service_result} | {question} -> {actual_service}")

        except Exception as ex:
            row = {
                "test_no": i,
                "section": test["section"],
                "question": question,
                "expected_service": expected_service,
                "actual_service": "ERROR",
                "service_result": "ERROR",
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
                "reply",
                "next_steps_url",
                "suggestions",
            ],
        )
        writer.writeheader()
        writer.writerows(results)

    summary = {}
    for row in results:
        key = row["service_result"]
        summary[key] = summary.get(key, 0) + 1

    print("\nTest summary:")
    for key, value in summary.items():
        print(f"{key}: {value}")

    print(f"\nSaved results to {output_file}")


if __name__ == "__main__":
    main()