import re
from services.bin_lookup import get_schedule, supported_districts_message


def extract_postcode(text):
    match = re.search(r"\bBD\d{1,2}\b", text.upper())
    return match.group(0) if match else None


def handle_query(user_input):
    data = get_schedule(user_input)

    if data:
        return format_bin_response(data)

    # more helpful fallback
    return (
        "Sorry — I couldn’t find bin collection info for that. "
        + supported_districts_message()
        + " You can also type your area name (e.g., 'Little Horton')."
    )



def format_bin_response(data):
    lines = [f"Bin collection info for {data['area_name']}:\n"]

    for c in data["collections"]:
        lines.append(
            f"{c['bin_type']} - {c['collection_day']} (Next: {c['next_collection_date']})"
        )

    return "\n".join(lines)
