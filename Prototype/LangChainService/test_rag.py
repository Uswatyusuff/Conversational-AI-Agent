from rag_store import search_rag

query = "How do I apply for a Blue Badge?"
results = search_rag(query, "Benefits & Support", 3)

for i, r in enumerate(results, start=1):
    print("=" * 80)
    print(f"Result {i}")
    print("Title:", r["title"])
    print("URL:", r["url"])
    print("Service:", r["service"])
    print(r["text"][:700])
    print()