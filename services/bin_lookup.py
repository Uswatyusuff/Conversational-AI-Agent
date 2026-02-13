import json
import os
import re

BASE_DIR = os.path.dirname(os.path.dirname(__file__))

DATA_PATH = os.path.join(BASE_DIR, "data", "bin_collection", "schedules.json")

with open(DATA_PATH, "r", encoding="utf-8") as f:
    DATA = json.load(f)

# Precompute supported districts and a quick lookup dict
DISTRICT_MAP = {d["postcode_district"].upper(): d for d in DATA["districts"]}
SUPPORTED_DISTRICTS = sorted(DISTRICT_MAP.keys())


def normalise_text(text: str) -> str:
    return re.sub(r"\s+", " ", text.strip().lower())


def extract_bd_district(text: str):
    """
    Extracts BD district from either:
    - 'BD7'
    - 'BD7 1AB'
    - 'bd7'
    """
    match = re.search(r"\b(BD\d{1,2})\b", text.upper())
    return match.group(1) if match else None


def get_schedule_by_district(district: str):
    if not district:
        return None
    return DISTRICT_MAP.get(district.upper())


def get_schedule_by_area(area_query: str):
    """
    Matches area_name loosely:
    - exact match
    - substring match (e.g. 'little horton')
    """
    if not area_query:
        return None

    q = normalise_text(area_query)
    for d in DATA["districts"]:
        area = normalise_text(d.get("area_name", ""))
        if q == area or q in area or area in q:
            return d
    return None


def supported_districts_message():
    return f"I currently support these postcode districts: {', '.join(SUPPORTED_DISTRICTS)}."


def get_schedule(user_input: str):
    """
    Main entry point: pass the *raw user input* and it tries:
    1) postcode district extraction
    2) area name matching
    """
    # 1) Try district lookup
    district = extract_bd_district(user_input)
    if district:
        data = get_schedule_by_district(district)
        return data

    # 2) Try area name lookup
    data = get_schedule_by_area(user_input)
    return data
