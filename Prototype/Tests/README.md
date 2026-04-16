# Bradford Council Chatbot — Test Suite

This directory contains two complementary test suites:

| Suite | Location | Technology | Purpose |
|-------|----------|------------|---------|
| **C# unit + integration** | `BradfordChatbot.Tests/` | xUnit · Moq · FluentAssertions | Fast, deterministic, stub-backed |
| **Python eval** | `eval/` | pytest · httpx | Live-server integration & quality |

---

## Prerequisites

### C# tests
- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) or later
- No running server required — all HTTP calls are stubbed

### Python eval tests
- Python 3.11 or later
- The Bradford Council chatbot running (default `http://localhost:8080`)
- Install dependencies:

```bash
cd Tests/eval
pip install -r requirements.txt
```

### Environment variables
Create `Tests/eval/.env` (or export as shell variables) to override defaults:

```ini
# URL of the running chatbot — default is shown
CHATBOT_BASE_URL=http://localhost:8080
```

---

## C# Unit & Integration Tests

### Run all tests

```bash
dotnet test Tests/BradfordChatbot.Tests/
```

### Run with detailed output

```bash
dotnet test Tests/BradfordChatbot.Tests/ --logger "console;verbosity=detailed"
```

### Run a single category

```bash
dotnet test Tests/BradfordChatbot.Tests/ --filter "FullyQualifiedName~Routing"
dotnet test Tests/BradfordChatbot.Tests/ --filter "FullyQualifiedName~Regression"
dotnet test Tests/BradfordChatbot.Tests/ --filter "FullyQualifiedName~Safeguarding"
```

### Run with coverage (requires `coverlet`)

```bash
dotnet test Tests/BradfordChatbot.Tests/ \
  --collect:"XPlat Code Coverage" \
  --results-directory ./TestResults
```

### Test categories

| Folder | What it tests |
|--------|--------------|
| `01_Routing/` | Intent detection, guard ordering, service routing |
| `02_ContextMemory/` | Session memory unit tests, follow-up context inheritance |
| `03_GDPR/` | PII masking, data not echoed, postcode sanitisation |
| `04_ResponseQuality/` | Banned phrases, reply length, robotic phrases |
| `05_Accuracy/` | RetrievalService cosine similarity, chunk isolation |
| `06_SuggestionChips/` | Chip presence, length, no duplicates, no loops |
| `07_Safeguarding/` | Urgent housing, domestic abuse, rough sleeping routing |
| `08_SpecialFlows/` | Bin lookup signal, appointment flow state machine |
| `09_Resilience/` | Edge inputs, typos, all-caps, extreme lengths |
| `10_Regression/` | Named regressions R1–R12 |
| `11_ApiContract/` | Response shape, service enum, signal format |
| `12_Performance/` | Guard latency < 100 ms, memory throughput |

### Important: WebApplicationFactory setup

If you want to use `Microsoft.AspNetCore.Mvc.Testing` for end-to-end C# tests (not currently active but scaffolded), add this line to `Program.cs`:

```csharp
// At the very end of Program.cs, outside the top-level statements:
public partial class Program { }
```

---

## Python Eval Tests (live server)

These tests require the chatbot to be running. Start it with:

```bash
cd BradfordChatbot          # the ASP.NET project root
dotnet run
```

and (in a separate terminal) the Python LangChain service:

```bash
cd LangChainService
uvicorn app:app --port 5050
```

### Run the full pytest suite

```bash
cd Tests/eval
pytest -v --timeout=30
```

### Run a specific file

```bash
pytest eval/test_routing_eval.py -v --timeout=30
pytest eval/test_response_quality_eval.py -v --timeout=30
```

### Run a specific test class or test

```bash
pytest eval/test_routing_eval.py::TestUrgentRouting -v
pytest eval/test_response_quality_eval.py::TestRegression::test_R9_eviction_risk_not_caught_by_name_guard -v
```

### Run only a category

```bash
pytest eval/ -v -k "Routing or routing"
pytest eval/ -v -k "Performance"
```

### Run with coverage

```bash
pytest eval/ --cov=eval --cov-report=html --timeout=30
```

### Pytest test files

| File | What it tests |
|------|--------------|
| `test_routing_eval.py` | 30+ routing cases, small-talk guard, vague-help, meaningless input, urgent housing |
| `test_response_quality_eval.py` | API contract, reply quality, context memory, regression R1–R12, basic performance |

---

## Standalone Evaluation Script

`eval/evaluate_responses.py` sends every case in `test_cases.json` to the live API and produces a pass/fail table plus a results CSV for trend tracking.

### Basic usage

```bash
cd Tests/eval
python evaluate_responses.py
```

### Common options

```bash
# Point at a non-default server
python evaluate_responses.py --url http://staging.example.com

# Run only routing cases
python evaluate_responses.py --category routing

# Stop on the first failure
python evaluate_responses.py --fail-fast

# Save results to a named file
python evaluate_responses.py --output results/sprint-42.csv

# Run without saving to CSV
python evaluate_responses.py --no-save
```

### Output

- Coloured pass/fail table in the terminal (requires `rich`; degrades to plain text if absent)
- `eval/results/eval_YYYYMMDD_HHMMSS.csv` by default
- Exit code `0` = all passed, `1` = any failures (suitable for CI)

---

## Test Data Files

| File | Description |
|------|-------------|
| `eval/test_cases.json` | Machine-readable test cases used by `evaluate_responses.py` |
| `eval/test_utterances.csv` | Human-readable spreadsheet of all utterances, expected services, required/banned phrases |

---

## CI Integration (GitHub Actions example)

```yaml
jobs:
  csharp-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.x' }
      - run: dotnet test Tests/BradfordChatbot.Tests/ --logger "trx;LogFileName=results.trx"
      - uses: actions/upload-artifact@v4
        with:
          name: test-results
          path: '**/results.trx'

  python-eval:
    runs-on: ubuntu-latest
    needs: csharp-tests
    services:
      chatbot:
        image: ghcr.io/your-org/bradford-chatbot:latest
        ports: ['8080:8080']
    env:
      CHATBOT_BASE_URL: http://localhost:8080
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-python@v5
        with: { python-version: '3.11' }
      - run: pip install -r Tests/eval/requirements.txt
      - run: pytest Tests/eval/ -v --timeout=30
```

---

## Adding New Tests

**C# tests** — add a new `.cs` file in the appropriate numbered folder and inherit from `ChatTestBase` for orchestrator tests, or write a plain xUnit class for pure unit tests.

**Python eval tests** — add a new test function or class to an existing file, or create a new `test_*.py` file in `eval/`. pytest discovers it automatically.

**Test data** — add rows to `eval/test_cases.json` (used by `evaluate_responses.py`) and/or `eval/test_utterances.csv` (human reference).
