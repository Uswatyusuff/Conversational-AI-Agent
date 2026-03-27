import os
import requests
from langchain.tools import tool

BACKEND_BASE_URL = os.getenv("BACKEND_BASE_URL", "http://councilchatbot:8080")


@tool
def lookup_addresses_by_postcode(postcode: str) -> str:
    """Find address options for a postcode using the backend postcode search endpoint."""
    try:
        r = requests.get(
            f"{BACKEND_BASE_URL}/api/postcode/search",
            params={"postcode": postcode},
            timeout=30
        )
        return r.text
    except Exception as e:
        return f"Tool error: {str(e)}"


@tool
def lookup_bin_result(postcode: str, address: str) -> str:
    """Get the bin collection result for a postcode and address."""
    try:
        r = requests.get(
            f"{BACKEND_BASE_URL}/api/postcode/bin-result",
            params={"postcode": postcode, "address": address},
            timeout=30
        )
        return r.text
    except Exception as e:
        return f"Tool error: {str(e)}"


@tool
def read_council_webpage(url: str) -> str:
    """Read a public council webpage and return page text."""
    try:
        r = requests.get(url, timeout=30)
        return r.text[:12000]
    except Exception as e:
        return f"Tool error: {str(e)}"