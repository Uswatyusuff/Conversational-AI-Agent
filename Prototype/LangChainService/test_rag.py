from rag_store import search_rag

TEST_QUERIES = [
    ("How much does a replacement bin cost?", "Waste & Bins"),
    ("Am I eligible for benefits?", "Benefits & Support"),
    ("Can I make a payment plan for council tax arrears?", "Council Tax"),
    ("How do I appeal a school place decision?", "Education"),
]

TOP_K = 5


def run_test(query: str, service: str, top_k: int = TOP_K) -> None:
    print("\n" + "#" * 100)
    print(f"QUERY   : {query}")
    print(f"SERVICE : {service}")
    print("#" * 100)

    results = search_rag(query, service, top_k)

    if not results:
        print("No results found.\n")
        return

    for i, r in enumerate(results, start=1):
        print("=" * 80)
        print(f"Result {i}")
        print("Title   :", r.get("title", ""))
        print("URL     :", r.get("url", ""))
        print("Service :", r.get("service", ""))
        if r.get("topic"):
            print("Topic   :", r.get("topic", ""))
        if r.get("heading"):
            print("Heading :", r.get("heading", ""))
        if r.get("section_hint"):
            print("Section :", r.get("section_hint", ""))
        print("-" * 80)
        print((r.get("text", "") or "")[:900])
        print()

    print()


if __name__ == "__main__":
    for query, service in TEST_QUERIES:
        run_test(query, service)