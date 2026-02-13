from openai import OpenAI
import os
from agent import handle_query

def main():

    USE_LLM_POLISH = True

    api_key = os.getenv("OPENAI_API_KEY")
    if USE_LLM_POLISH and not api_key:
        print("WARNING: OPENAI_API_KEY not set. Disabling LLM polishing.\n")
        USE_LLM_POLISH = False

    client = OpenAI() if USE_LLM_POLISH else None

    print("Bradford Council Bin Collection MVP (type 'exit' to quit)\n")

    while True:
        user_input = input("You: ").strip()
        if user_input.lower() == "exit":
            break

        # Deterministic agent logic (extract postcode -> JSON lookup -> factual reply)
        base_reply = handle_query(user_input)

        # use LLM only to improve wording (facts must not change)
        if USE_LLM_POLISH and client is not None:
            reply = polish_with_llm(client, user_input, base_reply)
        else:
            reply = base_reply

        print(f"Agent: {reply}\n")

def polish_with_llm(client: OpenAI, user_input: str, base_reply: str) -> str:
    """
    Uses the LLM only to improve clarity/tone.
    It must NOT change any facts, dates, or numbers in base_reply.
    """
    messages = [
        {
            "role": "system",
            "content": (
                "You are a public service assistant. Rewrite responses to be clear and friendly, "
                "but DO NOT change any facts, dates, or numbers. "
                "Do not add new information. Keep it short."
            )
        },
        {
            "role": "user",
            "content": (
                f"User asked: {user_input}\n\n"
                f"Factual response (must not change facts):\n{base_reply}\n\n"
                "Rewrite this response."
            )
        }
    ]

    try:
        response = client.chat.completions.create(
            model="gpt-4o-mini",
            messages=messages
        )
        return response.choices[0].message.content.strip()
    except Exception:
        # If the LLM fails for any reason, fall back to deterministic output
        return base_reply

if __name__ == "__main__":
    main()
