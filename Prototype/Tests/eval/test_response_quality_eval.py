"""
test_response_quality_eval.py
───────────────────────────────────────────────────────────────────────────────
Integration tests for response quality, contract, context memory,
and regression scenarios against the live chatbot API.

Run with:
    pytest eval/test_response_quality_eval.py -v --timeout=30
───────────────────────────────────────────────────────────────────────────────
"""

import pytest
import time
from conftest import (
    assert_response_shape,
    assert_no_banned_phrases,
    assert_known_service,
    KNOWN_SERVICES
)


# ── API contract ───────────────────────────────────────────────────────────────

class TestApiContract:

    @pytest.mark.parametrize("message", [
        "How do I pay my council tax?",
        "hi",
        "asdf",
        "I am John",
        "I need help",
        "I'm homeless",
    ])
    def test_response_has_all_required_fields(self, chat, message):
        """Every response must contain reply, service, nextStepsUrl, suggestions."""
        response = chat(message)
        assert_response_shape(response)

    @pytest.mark.parametrize("message", [
        "How do I pay my council tax?",
        "hi",
        "asdf",
        "I am homeless",
    ])
    def test_service_is_always_a_known_value(self, chat, message):
        """Service must always be one of the known service names."""
        response = chat(message)
        assert_known_service(response["service"])

    @pytest.mark.parametrize("message", [
        "hi",
        "How do I pay my council tax?",
        "asdf",
    ])
    def test_suggestions_is_always_a_list(self, chat, message):
        """suggestions must always be a list, never null."""
        response = chat(message)
        assert isinstance(response["suggestions"], list), (
            f"suggestions must be a list. Got: {type(response['suggestions'])}"
        )

    @pytest.mark.parametrize("message", [
        "hi",
        "How do I pay my council tax?",
        "asdf",
        "I am homeless",
    ])
    def test_reply_is_always_non_empty(self, chat, message):
        """reply must never be empty for any input."""
        response = chat(message)
        assert response["reply"].strip(), (
            f"Reply must not be empty for message: {message!r}"
        )

    def test_contact_us_includes_phone_number(self, chat):
        """Contact Us reply must include the Bradford Council phone number."""
        response = chat("how can I contact the council?")
        assert "01274 431000" in response["reply"], (
            f"Contact Us reply must include 01274 431000.\nReply: {response['reply']!r}"
        )

    def test_next_steps_url_is_bradford_if_present(self, chat):
        """If nextStepsUrl is non-empty, it must be a Bradford Council URL."""
        response = chat("How do I pay my council tax?")
        url = response.get("nextStepsUrl", "")
        if url:
            assert "bradford.gov.uk" in url, (
                f"nextStepsUrl must be a Bradford Council URL.\nGot: {url!r}"
            )


# ── Response quality ───────────────────────────────────────────────────────────

class TestResponseQuality:

    QUALITY_MESSAGES = [
        "How do I pay my council tax?",
        "When is my bin collection?",
        "Am I eligible for a blue badge?",
        "How do I apply for free school meals?",
        "I am homeless",
        "planning application status",
    ]

    @pytest.mark.parametrize("message", QUALITY_MESSAGES)
    def test_no_banned_phrases_in_service_replies(self, chat, message):
        """Service replies must not contain internal context-reference phrases."""
        response = chat(message)
        assert_no_banned_phrases(response["reply"])

    @pytest.mark.parametrize("message", QUALITY_MESSAGES)
    def test_service_replies_are_substantive(self, chat, message):
        """Service replies must be at least 30 characters long."""
        response = chat(message)
        assert len(response["reply"]) >= 30, (
            f"Reply for '{message}' is too short ({len(response['reply'])} chars): "
            f"{response['reply']!r}"
        )

    @pytest.mark.parametrize("message", QUALITY_MESSAGES)
    def test_no_robotic_phrases(self, chat, message):
        """Replies must not contain AI self-identification phrases."""
        response = chat(message)
        robotic = [
            "as an ai language model",
            "i am an ai",
            "as a large language model",
            "i'm a chatbot",
        ]
        reply_lower = response["reply"].lower()
        for phrase in robotic:
            assert phrase not in reply_lower, (
                f"Reply for '{message}' contains robotic phrase '{phrase}'"
            )

    def test_greeting_reply_is_appropriate_length(self, chat):
        """Greeting replies should be brief — between 5 and 200 characters."""
        response = chat("hello")
        length = len(response["reply"])
        assert 5 <= length <= 200, (
            f"Greeting reply length {length} is outside expected range [5, 200]. "
            f"Reply: {response['reply']!r}"
        )

    @pytest.mark.parametrize("message", [
        "hi", "hello", "How do I pay my council tax?", "I am homeless"
    ])
    def test_suggestions_are_not_single_words(self, chat, message):
        """Suggestion chips must be substantive phrases, not single words."""
        response = chat(message)
        for chip in response["suggestions"]:
            assert len(chip) > 9, (
                f"Chip '{chip}' is too short for message '{message}'. "
                f"Chips must be full phrases to avoid clarification loops."
            )


# ── Context / memory tests ────────────────────────────────────────────────────

class TestContextMemory:

    def test_greeting_after_council_tax_does_not_inherit_context(self, multi_turn_chat):
        """R1: 'hi' after a council tax answer must not inherit council tax context."""
        session = multi_turn_chat()
        session("How do I pay my council tax?")
        response = session("hi")

        assert response["service"] == "Unknown", (
            f"Greeting after council tax was routed to '{response['service']}'"
        )
        reply_lower = response["reply"].lower()
        contamination = ["council tax", "payment", "direct debit"]
        for phrase in contamination:
            assert phrase not in reply_lower, (
                f"Greeting reply contains council-tax content: '{phrase}'"
            )

    def test_short_followup_inherits_service_context(self, multi_turn_chat):
        """Short follow-up should carry previous service context."""
        session = multi_turn_chat()
        session("How do I pay my council tax?")
        response = session("how much does it cost?")

        assert response["service"] == "Council Tax", (
            f"Follow-up 'how much does it cost?' was routed to '{response['service']}'"
        )

    def test_topic_switch_updates_service(self, multi_turn_chat):
        """Explicit new service query should override previous context."""
        session = multi_turn_chat()
        session("When is my bin collection?")
        response = session("How do I pay my council tax?")

        assert response["service"] == "Council Tax", (
            f"Topic switch to council tax returned service '{response['service']}'"
        )

    def test_reset_phrase_clears_context(self, multi_turn_chat):
        """'something else' should clear context and return a generic prompt."""
        session = multi_turn_chat()
        session("When is my bin collection?")
        response = session("something else")

        # The response should not maintain bin context
        reply_lower = response["reply"].lower()
        assert "bin collection" not in reply_lower, (
            "Reset phrase should clear bin context from reply"
        )


# ── Regression ────────────────────────────────────────────────────────────────

class TestRegression:

    def test_R1_greeting_after_council_tax(self, multi_turn_chat):
        """R1: greeting after council tax must not return council tax content."""
        session = multi_turn_chat()
        session("How do I pay my council tax?")
        response = session("hi")
        assert response["service"] == "Unknown"
        reply_lower = response["reply"].lower()
        assert "council tax" not in reply_lower
        assert "direct debit" not in reply_lower

    def test_R2_idk_does_not_return_blue_badge(self, chat):
        """R2: 'idk' must not return Blue Badge content."""
        response = chat("idk")
        assert "blue badge" not in response["reply"].lower()
        assert response["service"] == "Unknown"

    def test_R3_gibberish_does_not_return_library_content(self, chat):
        """R3: 'sdfsadf' must not return Library content."""
        response = chat("sdfsadf")
        assert "library" not in response["reply"].lower()
        assert response["service"] == "Unknown"

    def test_R4_name_introduction_not_routed_to_housing(self, chat):
        """R4: 'I am John' must not route to Housing."""
        response = chat("I am John")
        assert response["service"] != "Housing", (
            "Name introduction 'I am John' was routed to Housing"
        )
        assert response["service"] == "Unknown"

    def test_R5_council_tax_support_not_student_discount(self, chat):
        """R5: Council Tax Support query must not mention student discount."""
        response = chat("How do I apply for council tax support?")
        assert "student" not in response["reply"].lower(), (
            "Council tax support reply mentioned 'student' — likely returning student discount"
        )

    def test_R6_disabled_people_routes_to_benefits(self, chat):
        """R6: 'What help is available for disabled people?' must route to Benefits."""
        response = chat("What help is available for disabled people?")
        assert response["service"] == "Benefits & Support", (
            f"Disability query routed to '{response['service']}' instead of Benefits & Support"
        )

    def test_R8_contact_council_returns_contact_details(self, chat):
        """R8: 'How can I contact the council?' must return contact details."""
        response = chat("How can I contact the council?")
        assert response["service"] == "Contact Us"
        assert "01274 431000" in response["reply"], (
            "Contact Us reply must include the council phone number"
        )

    def test_R9_eviction_risk_not_caught_by_name_guard(self, chat):
        """R9: 'I am at risk of eviction' must route to Housing, not produce a greeting."""
        response = chat("I am at risk of eviction")
        assert "Hello" not in response["reply"], (
            "'I am at risk of eviction' was mistakenly handled as a name introduction"
        )
        assert response["service"] == "Housing"

    def test_R12_help_paying_bills_not_vague_help(self, chat):
        """R12: 'I need help paying bills' must not trigger vague-help guard."""
        response = chat("I need help paying bills")
        assert response["service"] != "Unknown", (
            "'I need help paying bills' was caught by vague-help guard"
        )


# ── Basic performance ─────────────────────────────────────────────────────────

class TestBasicPerformance:

    PERFORMANCE_LIMIT_SECONDS = 10  # generous limit for live server

    @pytest.mark.parametrize("message", [
        "hi", "asdf", "I am John", "I need help"
    ])
    def test_guard_level_responses_are_fast(self, chat, message):
        """Guard-level responses (no LLM call) must respond under 10s."""
        start = time.time()
        chat(message)
        elapsed = time.time() - start

        assert elapsed < self.PERFORMANCE_LIMIT_SECONDS, (
            f"Guard response for '{message}' took {elapsed:.2f}s (limit: {self.PERFORMANCE_LIMIT_SECONDS}s)"
        )

    def test_ten_sequential_greetings_are_not_slow(self, http_client):
        """10 sequential greetings must not take more than 15s total."""
        start = time.time()
        for i in range(10):
            r = http_client.post("/api/chat", json={
                "message": "hello",
                "sessionId": f"perf-{i}"
            })
            assert r.status_code == 200
        elapsed = time.time() - start

        assert elapsed < 15, (
            f"10 sequential greetings took {elapsed:.2f}s (expected < 15s)"
        )
