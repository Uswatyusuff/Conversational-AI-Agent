"""
test_routing_eval.py
───────────────────────────────────────────────────────────────────────────────
Integration tests for routing and intent detection against the live chatbot API.

Run with:
    pytest eval/test_routing_eval.py -v --timeout=30

Requirements:
    - Chatbot running at CHATBOT_BASE_URL (default: http://localhost:8080)
    - pip install -r eval/requirements.txt
───────────────────────────────────────────────────────────────────────────────
"""

import pytest
from conftest import assert_response_shape, assert_no_banned_phrases, assert_known_service


# ── Service routing ────────────────────────────────────────────────────────────

class TestServiceRouting:

    @pytest.mark.parametrize("message,expected_service", [
        ("How do I pay my council tax?",                    "Council Tax"),
        ("What is my council tax balance?",                 "Council Tax"),
        ("I need help paying my bill",                      "Council Tax"),
        ("How do I set up a direct debit for council tax?", "Council Tax"),
        ("council tax arrears help",                        "Council Tax"),
        ("When is my bin collection?",                      "Waste & Bins"),
        ("My bin wasn't collected",                         "Waste & Bins"),
        ("I need a new recycling bin",                      "Waste & Bins"),
        ("how do I report a missed bin",                    "Waste & Bins"),
        ("How do I apply for a Blue Badge?",                "Benefits & Support"),
        ("Am I eligible for benefits?",                     "Benefits & Support"),
        ("PIP application",                                 "Benefits & Support"),
        ("free school meals eligibility",                   "Benefits & Support"),
        ("What help is available for disabled people?",     "Benefits & Support"),
        ("How do I apply for a school place?",              "Education"),
        ("school admissions deadline",                      "Education"),
        ("EHCP transport",                                  "Education"),
        ("I need housing help",                             "Housing"),
        ("I'm homeless",                                    "Housing"),
        ("eviction help",                                   "Housing"),
        ("planning application status",                     "Planning"),
        ("planning permission",                             "Planning"),
        ("library hours",                                   "Libraries"),
        ("renew library books",                             "Libraries"),
        ("how can I contact the council?",                  "Contact Us"),
        ("what is the council phone number?",               "Contact Us"),
        ("book an appointment",                             "Appointment"),
        ("I'd like to speak to someone",                    "Appointment"),
        ("nearest recycling centre to me",                  "Location"),
        ("find a library near me",                          "Location"),
    ])
    def test_service_routing(self, chat, message, expected_service):
        """Verify each message routes to the correct service."""
        response = chat(message)
        assert_response_shape(response)
        assert response["service"] == expected_service, (
            f"Message: {message!r}\n"
            f"Expected service: {expected_service}\n"
            f"Got service: {response['service']}\n"
            f"Reply: {response['reply']!r}"
        )


class TestSmallTalkGuard:

    @pytest.mark.parametrize("message", [
        "hi", "hello", "hey", "good morning", "good evening",
        "thanks", "thank you", "cheers", "bye", "goodbye",
    ])
    def test_smalltalk_routes_to_unknown(self, chat, message):
        """Greetings and farewells must never route to a service."""
        response = chat(message)
        assert_response_shape(response)
        assert response["service"] == "Unknown", (
            f"Greeting '{message}' was misrouted to '{response['service']}'"
        )
        assert_no_banned_phrases(response["reply"])

    @pytest.mark.parametrize("message", [
        "I am John",
        "I am Sarah",
        "my name is Ahmed",
        "I'm David",
    ])
    def test_name_introduction_is_greeting(self, chat, message):
        """Name introductions must produce friendly greetings, not service routing."""
        response = chat(message)
        assert response["service"] == "Unknown", (
            f"Name intro '{message}' was routed to '{response['service']}'"
        )
        reply_lower = response["reply"].lower()
        assert any(w in reply_lower for w in ["hello", "hi"]), (
            f"Name intro reply should be a greeting. Got: {response['reply']!r}"
        )


class TestVagueHelpGuard:

    @pytest.mark.parametrize("message", [
        "help",
        "I need help",
        "can you help me",
        "help me please",
    ])
    def test_vague_help_returns_service_menu(self, chat, message):
        """Vague help must return a service menu, not a random service answer."""
        response = chat(message)
        assert response["service"] == "Unknown"
        assert len(response["suggestions"]) > 0, (
            "Vague help must include service menu suggestions"
        )

    def test_help_with_service_noun_bypasses_guard(self, chat):
        """'I need help with my bins' has a service noun and must not trigger vague-help."""
        response = chat("I need help with my bins")
        assert response["service"] != "Unknown", (
            "A message with a service noun must bypass the vague-help guard"
        )


class TestMeaninglessInputGuard:

    @pytest.mark.parametrize("message", [
        "asdf", "sdfsadf", "zzzzz", "qwerty", "aaaaaa",
        "idk", "not sure",
    ])
    def test_meaningless_input_does_not_leak_service_content(self, chat, message):
        """Meaningless input must be intercepted before RAG retrieval."""
        response = chat(message)
        assert response["service"] == "Unknown"
        reply_lower = response["reply"].lower()
        contamination = [
            "blue badge", "library", "renew books",
            "planning application", "council tax band"
        ]
        for phrase in contamination:
            assert phrase not in reply_lower, (
                f"Meaningless input '{message}' returned service content: '{phrase}'"
            )


class TestUrgentRouting:

    @pytest.mark.parametrize("message", [
        "I am homeless",
        "I have nowhere to sleep tonight",
        "I'm being evicted tomorrow",
        "I need emergency housing tonight",
        "I am sleeping rough",
    ])
    def test_urgent_housing_routes_directly(self, chat, message):
        """Urgent housing messages must route directly to Housing."""
        response = chat(message)
        assert response["service"] == "Housing", (
            f"Urgent message '{message}' was routed to '{response['service']}'"
        )
        # Must not fall to generic clarification
        clarification_phrases = [
            "i didn't quite catch",
            "i'm not sure what you mean",
            "could you tell me a bit more"
        ]
        reply_lower = response["reply"].lower()
        for phrase in clarification_phrases:
            assert phrase not in reply_lower, (
                f"Urgent message '{message}' fell to clarification: {phrase!r}"
            )
