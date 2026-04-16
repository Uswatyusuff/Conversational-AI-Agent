"""
conftest.py
───────────────────────────────────────────────────────────────────────────────
Shared pytest fixtures for the Bradford Council Chatbot integration eval suite.

Configuration:
  Set CHATBOT_BASE_URL in .env or as an env var to point to your running chatbot.
  Default: http://localhost:8080

Usage:
  pytest eval/ -v --timeout=30
───────────────────────────────────────────────────────────────────────────────
"""

import os
import uuid
import pytest
import httpx
from dotenv import load_dotenv

load_dotenv()

# ── Configuration ──────────────────────────────────────────────────────────────

BASE_URL = os.getenv("CHATBOT_BASE_URL", "http://localhost:8080")
DEFAULT_TIMEOUT = 30  # seconds per request


# ── Fixtures ───────────────────────────────────────────────────────────────────

@pytest.fixture(scope="session")
def base_url() -> str:
    """The chatbot API base URL, configurable via CHATBOT_BASE_URL env var."""
    return BASE_URL.rstrip("/")


@pytest.fixture(scope="session")
def http_client(base_url):
    """
    Session-scoped httpx client.
    Re-used across all tests in the session for efficiency.
    """
    with httpx.Client(base_url=base_url, timeout=DEFAULT_TIMEOUT) as client:
        yield client


@pytest.fixture
def session_id() -> str:
    """
    Unique session ID per test.
    Using a UUID ensures no state leakage between tests.
    """
    return f"pytest-{uuid.uuid4().hex[:12]}"


@pytest.fixture
def chat(http_client, session_id):
    """
    Convenience fixture: returns a callable that sends a chat message
    and returns the parsed JSON response dict.

    Usage:
        def test_something(chat):
            response = chat("How do I pay my council tax?")
            assert response["service"] == "Council Tax"
    """
    def _chat(message: str, custom_session: str | None = None) -> dict:
        payload = {
            "message": message,
            "sessionId": custom_session or session_id
        }
        r = http_client.post("/api/chat", json=payload)
        r.raise_for_status()
        return r.json()

    return _chat


@pytest.fixture
def multi_turn_chat(http_client):
    """
    Fixture for multi-turn conversation tests.
    Returns a factory that creates a session-bound chat function.

    Usage:
        def test_context(multi_turn_chat):
            session = multi_turn_chat()
            r1 = session("How do I pay council tax?")
            r2 = session("how do I do that online?")
            assert r2["service"] == "Council Tax"
    """
    def _make_session():
        sid = f"multi-{uuid.uuid4().hex[:12]}"

        def _chat(message: str) -> dict:
            r = http_client.post("/api/chat", json={"message": message, "sessionId": sid})
            r.raise_for_status()
            return r.json()

        return _chat

    return _make_session


# ── Shared assertion helpers ───────────────────────────────────────────────────

BANNED_PHRASES = [
    "context does not provide",
    "context does not specify",
    "the context does not",
    "not specified in the context",
    "not mentioned in the context",
    "based on the context provided",
    "according to the context",
    "i am not able to find",
    "i'm not able to find",
    "i cannot find",
    "i can't find",
    "as an ai language model",
    "i'm a chatbot",
]


def assert_no_banned_phrases(reply: str):
    """Assert the reply contains none of the known implementation-leakage phrases."""
    lower = reply.lower()
    for phrase in BANNED_PHRASES:
        assert phrase not in lower, (
            f"Reply contains banned phrase '{phrase}'.\nReply: {reply!r}"
        )


def assert_response_shape(response: dict):
    """Assert the response has all required fields with correct types."""
    assert "reply" in response,          "Response missing 'reply' field"
    assert "service" in response,        "Response missing 'service' field"
    assert "nextStepsUrl" in response,   "Response missing 'nextStepsUrl' field"
    assert "suggestions" in response,    "Response missing 'suggestions' field"

    assert isinstance(response["reply"], str),            "reply must be a string"
    assert isinstance(response["service"], str),          "service must be a string"
    assert isinstance(response["nextStepsUrl"], str),     "nextStepsUrl must be a string"
    assert isinstance(response["suggestions"], list),     "suggestions must be a list"

    assert response["reply"],                             "reply must not be empty"
    assert response["service"],                           "service must not be empty"


KNOWN_SERVICES = {
    "Unknown", "Council Tax", "Waste & Bins", "Benefits & Support",
    "Education", "Housing", "Planning", "Libraries", "Contact Us",
    "Appointment", "Form Assistant", "Location"
}


def assert_known_service(service: str):
    """Assert the service field is one of the known values."""
    assert service in KNOWN_SERVICES, (
        f"Service '{service}' is not a known service name. "
        f"Known: {sorted(KNOWN_SERVICES)}"
    )
