# Bradford Council — Citizen Support AI

A production-grade AI chatbot for Bradford Council that helps citizens get instant answers about council services. Built on ASP.NET Core 10, LangChain, FAISS vector search, and GPT-4o-mini, deployed as three Docker containers on AWS EC2.

---

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Tech Stack](#tech-stack)
- [Services](#services)
- [Project Structure](#project-structure)
- [Prerequisites](#prerequisites)
- [Local Development](#local-development)
- [Docker Deployment — Local](#docker-deployment--local)
- [Docker Deployment — AWS EC2 Production](#docker-deployment--aws-ec2-production)
- [CI/CD with GitHub Actions](#cicd-with-github-actions)
- [Environment Variables](#environment-variables)
- [API Endpoints](#api-endpoints)
- [Running Tests](#running-tests)
- [Configuration](#configuration)
- [Adding or Updating FAQ Content](#adding-or-updating-faq-content)

---

## Overview

Bradford Council Citizen Support AI is a multi-service chatbot that handles citizen queries across 12 council service areas. It combines rule-based intent routing, retrieval-augmented generation (RAG) over scraped Bradford Council pages, and a LangChain agent for complex multi-turn reasoning.

**Supported service areas:**
Council Tax · Waste & Bins · Benefits & Support · School Admissions · Planning · Libraries · Housing Support · Book Appointment · Find Nearby Services · Form Assistant · Payment Calculator · School Finder

**Key capabilities:**
- Crisis / safeguarding detection fires before all other logic
- Session-isolated conversation memory with 30-minute TTL
- Bin collection day lookup via Playwright browser automation
- Postcode-to-address resolution via postcodes.io
- Feedback collection with satisfaction dashboard
- Voice input and text-to-speech output
- Themeable UI (Dark, Light, Midnight, Warm) with font and layout controls

---

## Architecture

```
Citizen (Browser)
        │
        ▼
┌─────────────────────┐
│  .NET Chatbot UI    │  ASP.NET Core 10 · Port 8080
│  (Vanilla JS SPA)   │
└────────┬────────────┘
         │  POST /api/chat
         ▼
┌─────────────────────┐
│  Chat Orchestrator  │  Intent routing · Guard chain · Session memory
│  (ChatOrchestrator) │
└──────┬──────┬───────┘
       │      │
       ▼      ▼
┌──────────┐  ┌─────────────────────┐
│ Embedding│  │  LangChain Agent    │  FastAPI · Port 8010
│ Service  │  │  (RAG + GPT-4o-mini)│
│ Port 8001│  └──────────┬──────────┘
└──────────┘             │
  sentence-              ▼
  transformers    FAISS Vector Store
  MiniLM-L6-v2   (Bradford Council pages)
```

**Guard chain order inside the orchestrator (fires top-to-bottom):**

1. Crisis / safeguarding — returns immediately, no LLM call
2. Small talk — greetings, thanks
3. Meaningless input — noise, gibberish
4. Vague help — "I need help" without context
5. Short follow-up carry-over — only fires when session has history
6. Context reset — "start again", "different question"
7. Service-specific handlers — bin lookup, appointments, housing, forms, calculator, school finder
8. RAG retrieval → LangChain agent

---

## Tech Stack

| Layer | Technology |
|---|---|
| Backend | ASP.NET Core 10 (C#), .NET 10 |
| Frontend | Vanilla JS, CSS custom properties, DM Sans |
| AI / LLM | OpenAI GPT-4o-mini via LangChain |
| Embeddings | `sentence-transformers/all-MiniLM-L6-v2` (CPU, self-hosted) |
| Vector search | FAISS (via LangChain Community) |
| Agent framework | LangChain + LangChain-OpenAI |
| Browser automation | Microsoft Playwright (bin collection lookup) |
| Containerisation | Docker + Docker Compose |
| CI/CD | GitHub Actions |
| Cloud | AWS EC2 (Ubuntu) |
| Registry | Docker Hub |

---

## Services

### 1. `councilchatbot` — ASP.NET Core (Port 8080)

The main application. Serves the frontend SPA and exposes all REST endpoints. Contains the full orchestration logic, conversation memory, and all service-specific handlers.

**Key files:**
- `Program.cs` — DI wiring, HTTP client registration, FAQ embedding bootstrap
- `Services/ChatOrchestrator.cs` — central routing hub
- `Services/ConversationMemory.cs` — session-keyed memory store
- `Services/EmbeddingService.cs` — HTTP client to embedding service
- `Services/LangChainClientService.cs` — HTTP client to LangChain service
- `Services/PlaywrightService.cs` — headless Chromium for bin lookup
- `wwwroot/` — SPA (index.html, app.js, styles.css)

### 2. `embeddingservice` — Python FastAPI (Port 8001)

Stateless sentence embedding service. Loads `all-MiniLM-L6-v2` on startup and exposes `/embed` and `/embed-batch` endpoints. Used by both the .NET app (FAQ chunk embeddings) and the LangChain service (FAISS query embedding).

**Key files:**
- `EmbeddingService/app.py`
- `EmbeddingService/requirements.txt`

### 3. `langchainservice` — Python FastAPI (Port 8010)

LangChain agent with FAISS RAG over scraped Bradford Council pages. Handles complex queries, intent classification, and tool-use (postcode lookup, bin result retrieval via back-calls to the .NET API).

**Key files:**
- `LangChainService/app.py` — FastAPI entrypoint
- `LangChainService/agent.py` — LangChain agent runner
- `LangChainService/tools.py` — intent detection, RAG search, postcode tools
- `LangChainService/rag_store.py` — FAISS load + document reranking
- `LangChainService/embedding_client.py` — calls embedding service
- `LangChainService/ingest.py` — builds the FAISS index from scraped data

---

## Project Structure

```
CouncilChatbotPrototype ver6/
├── Program.cs                        # App entry point, DI, FAQ bootstrap
├── CouncilChatbotPrototype.csproj
├── appsettings.json                  # Service URLs, OpenAI model, Playwright config
├── appsettings.Development.json
├── .env.example                      # Copy to .env and fill in keys
│
├── Controllers/                      # REST API controllers
│   ├── ChatController.cs             # POST /api/chat
│   ├── FeedbackController.cs         # POST /api/feedback
│   ├── PostcodeController.cs         # GET  /api/postcode/search
│   ├── AppointmentController.cs
│   ├── FormFlowController.cs
│   ├── LocationController.cs
│   ├── SchoolController.cs
│   └── VoiceController.cs
│
├── Services/                         # Business logic
│   ├── ChatOrchestrator.cs           # Central routing hub
│   ├── ConversationMemory.cs         # Session memory (ConcurrentDictionary + TTL)
│   ├── EmbeddingService.cs           # HTTP → embeddingservice:8001
│   ├── LangChainClientService.cs     # HTTP → langchainservice:8010
│   ├── RetrievalService.cs           # Cosine similarity vector search
│   ├── PlaywrightService.cs          # Headless Chromium bin lookup
│   ├── AppointmentService.cs
│   ├── FormFlowService.cs
│   ├── HousingNavigatorService.cs
│   ├── CouncilTaxCalculatorService.cs
│   ├── SchoolFinderService.cs
│   └── LocationService.cs
│
├── Models/                           # Request / response models
├── Data/                             # FAQ JSON + cached embeddings
│   ├── faqs.json                     # FAQ source content
│   └── chunks.embeddings.json        # Pre-computed embedding cache
│
├── wwwroot/                          # Static frontend SPA
│   ├── index.html
│   ├── app.js
│   └── styles.css
│
├── EmbeddingService/                 # Python embedding microservice
│   ├── app.py
│   ├── Dockerfile
│   └── requirements.txt
│
├── LangChainService/                 # Python LangChain microservice
│   ├── app.py
│   ├── agent.py
│   ├── tools.py
│   ├── rag_store.py
│   ├── embedding_client.py
│   ├── ingest.py                     # Run once to build FAISS index
│   ├── Dockerfile
│   ├── requirements.txt
│   └── data/faiss_index/             # FAISS index files (required at runtime)
│
├── Infrastructure/
│   ├── Dockerfile                    # Multi-stage .NET build
│   ├── docker-compose.yml            # Local development (builds from source)
│   └── docker-compose.prod.yml       # Production (pulls from Docker Hub)
│
├── Tests/
│   ├── BradfordChatbot.Tests/        # xUnit C# test suite (12 categories)
│   └── eval/                         # Python pytest eval suite (live server)
│
└── .github/
    └── workflows/
        └── deploy.yml                # CI/CD: build → push → deploy to EC2
```

---

## Prerequisites

| Requirement | Version | Used for |
|---|---|---|
| .NET SDK | 10.0+ | Running / building the .NET app |
| Python | 3.11+ | Embedding service, LangChain service, eval tests |
| Docker Desktop | Latest | Container builds |
| Docker Compose plugin | v2+ | `docker compose` (with a space) |
| OpenAI API key | — | GPT-4o-mini responses |

---

## Local Development

### 1. Clone and set up environment

```bash
git clone https://github.com/YOUR_USERNAME/bradford-council-chatbot.git
cd "bradford-council-chatbot"

cp .env.example .env
# Edit .env and add your OpenAI API key
```

### 2. Build the FAISS index (one-time)

The LangChain service needs a FAISS index before it can answer questions. If `LangChainService/data/faiss_index/` is not already present:

```bash
cd LangChainService
pip install -r requirements.txt
EMBEDDING_URL=http://localhost:8001/embed python ingest.py
cd ..
```

> The embedding service must be running locally on port 8001 for this step.

### 3. Run without Docker

**Terminal 1 — Embedding service:**
```bash
cd EmbeddingService
pip install -r requirements.txt
uvicorn app:app --host 0.0.0.0 --port 8001
```

**Terminal 2 — LangChain service:**
```bash
cd LangChainService
uvicorn app:app --host 0.0.0.0 --port 8010
```

**Terminal 3 — .NET chatbot:**
```bash
dotnet run
# App available at http://localhost:8080
```

---

## Docker Deployment — Local

Run everything from the **project root** (not from `Infrastructure/`):

```bash
# Build and start all three containers
docker compose -f Infrastructure/docker-compose.yml up --build

# Detached (background)
docker compose -f Infrastructure/docker-compose.yml up --build -d

# View live logs
docker compose -f Infrastructure/docker-compose.yml logs -f

# Stop everything
docker compose -f Infrastructure/docker-compose.yml down
```

Docker Compose reads `.env` from the current working directory, so running from the project root picks up your API key automatically.

**Startup order** (enforced by healthchecks):
1. `embeddingservice` starts and loads the model (~60 s on cold start)
2. `langchainservice` starts once embedding is healthy (~30 s)
3. `councilchatbot` starts once both are healthy (builds FAQ embeddings if not cached)

Total cold-start time: 3–5 minutes on first run. Subsequent starts are fast because embedding results are cached in `Data/chunks.embeddings.json`.

**Verify all three are healthy:**
```bash
docker compose -f Infrastructure/docker-compose.yml ps
# All three should show: Status: healthy

curl http://localhost:8001/health   # {"status":"ok","model":"sentence-transformers/..."}
curl http://localhost:8010/health   # {"status":"ok","model":"gpt-4o-mini"}
curl http://localhost:8080/health   # {"status":"ok"}
```

---

## Docker Deployment — AWS EC2 Production

### Step 1 — Build and push images to Docker Hub

From your local machine, in the project root:

```bash
docker login

docker build -t YOUR_DOCKERHUB_USER/councilchatbot:latest \
  -f Infrastructure/Dockerfile .

docker build -t YOUR_DOCKERHUB_USER/embeddingservice:latest \
  EmbeddingService/

docker build -t YOUR_DOCKERHUB_USER/langchainservice:latest \
  LangChainService/

docker push YOUR_DOCKERHUB_USER/councilchatbot:latest
docker push YOUR_DOCKERHUB_USER/embeddingservice:latest
docker push YOUR_DOCKERHUB_USER/langchainservice:latest
```

### Step 2 — One-time EC2 setup

```bash
ssh -i your-key.pem ubuntu@YOUR_EC2_IP

sudo apt-get update && sudo apt-get install -y docker.io docker-compose-plugin
sudo usermod -aG docker ubuntu && newgrp docker
mkdir -p ~/councilchatbot/Infrastructure
```

### Step 3 — Copy the production compose file

From your local machine:
```bash
scp -i your-key.pem \
  Infrastructure/docker-compose.prod.yml \
  ubuntu@YOUR_EC2_IP:~/councilchatbot/Infrastructure/
```

### Step 4 — Create `.env` on the VM

SSH into the VM and create it directly — never SCP a file containing real keys:

```bash
cat > ~/councilchatbot/.env << 'EOF'
OPENAI_API_KEY=sk-proj-YOUR_KEY_HERE
DOCKER_USERNAME=YOUR_DOCKERHUB_USERNAME
EOF
chmod 600 ~/councilchatbot/.env
```

### Step 5 — Deploy

```bash
cd ~/councilchatbot
docker compose -f Infrastructure/docker-compose.prod.yml pull
docker compose -f Infrastructure/docker-compose.prod.yml up -d --remove-orphans
docker image prune -f
```

### Step 6 — Open port 8080 in AWS

In the AWS Console: **EC2 → Security Groups → your instance → Inbound rules → Edit inbound rules**

| Type | Port range | Source |
|---|---|---|
| Custom TCP | 8080 | 0.0.0.0/0 |

The app is then available at `http://YOUR_EC2_IP:8080`.

### Redeploy after a code change

```bash
# Local machine
docker build -t YOUR_DOCKERHUB_USER/councilchatbot:latest -f Infrastructure/Dockerfile .
docker push YOUR_DOCKERHUB_USER/councilchatbot:latest

# EC2
cd ~/councilchatbot
docker compose -f Infrastructure/docker-compose.prod.yml pull
docker compose -f Infrastructure/docker-compose.prod.yml up -d --remove-orphans
```

---

## CI/CD with GitHub Actions

The workflow at `.github/workflows/deploy.yml` runs automatically on every push to `main`:

1. Builds all three Docker images
2. Pushes them to Docker Hub
3. SSHes into EC2 and runs `docker compose pull && up -d`

### Required GitHub Secrets

Go to **Settings → Secrets and variables → Actions** and add:

| Secret | Description |
|---|---|
| `DOCKER_USERNAME` | Your Docker Hub username |
| `DOCKER_PASSWORD` | Your Docker Hub password or access token |
| `OPENAI_API_KEY` | Your OpenAI API key |
| `EC2_HOST` | Public IP or DNS of your EC2 instance |
| `EC2_USER` | SSH username (typically `ubuntu`) |
| `EC2_SSH_KEY` | Full contents of your `.pem` private key file |

---

## Environment Variables

### Root `.env` (local development)

```ini
OPENAI_API_KEY=sk-proj-...
DOCKER_USERNAME=yourdockerhubusername
```

### docker-compose environment (set automatically)

| Variable | Service | Value |
|---|---|---|
| `OpenAI__ApiKey` | councilchatbot | From `OPENAI_API_KEY` |
| `EmbeddingService__BaseUrl` | councilchatbot | `http://embeddingservice:8001` |
| `LangChain__BaseUrl` | councilchatbot | `http://langchainservice:8010` |
| `OPENAI_API_KEY` | langchainservice | From `OPENAI_API_KEY` |
| `EMBEDDING_URL` | langchainservice | `http://embeddingservice:8001/embed` |
| `BACKEND_BASE_URL` | langchainservice | `http://councilchatbot:8080` |

> **Note:** `appsettings.json` defaults to `http://127.0.0.1:8001` and `http://127.0.0.1:8010` for running without Docker. The environment variables above override these at runtime inside containers.

---

## API Endpoints

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/chat` | Send a message, get a reply |
| `POST` | `/api/feedback` | Submit feedback for a response |
| `GET` | `/api/postcode/search` | Look up addresses from a postcode |
| `GET` | `/api/postcode/bin-result` | Get bin collection day for an address |
| `POST` | `/api/appointment` | Book or query appointment slots |
| `POST` | `/api/form/start` | Start a guided form flow |
| `POST` | `/api/form/answer` | Submit an answer in a form flow |
| `GET` | `/api/location` | Find nearby council services |
| `GET` | `/api/school` | Search schools by name or postcode |
| `GET` | `/health` | Health check (used by Docker) |

### Chat request / response shape

```json
// POST /api/chat
{
  "message": "How do I pay my council tax?",
  "sessionId": "a1b2c3d4-..."
}

// Response
{
  "reply": "You can pay your Council Tax online at bradford.gov.uk...",
  "service": "Council Tax",
  "nextStepsUrl": "https://www.bradford.gov.uk/council-tax/pay-your-council-tax/",
  "score": 0.87,
  "suggestions": ["Set up Direct Debit", "Check my balance", "Apply for a discount"]
}
```

---

## Running Tests

### C# xUnit tests (no server required)

```bash
# Run all 12 test categories at once
dotnet test Tests/BradfordChatbot.Tests/

# With detailed output
dotnet test Tests/BradfordChatbot.Tests/ --logger "console;verbosity=detailed"

# Single category
dotnet test Tests/BradfordChatbot.Tests/ --filter "FullyQualifiedName~Safeguarding"

# With code coverage
dotnet test Tests/BradfordChatbot.Tests/ \
  --collect:"XPlat Code Coverage" \
  --results-directory ./TestResults
```

| Category | What it covers |
|---|---|
| `01_Routing` | Intent detection, guard ordering |
| `02_ContextMemory` | Session isolation, follow-up context |
| `03_GDPR` | PII masking, postcode sanitisation |
| `04_ResponseQuality` | Banned phrases, reply length |
| `05_Accuracy` | Cosine similarity, chunk retrieval |
| `06_SuggestionChips` | Chip presence, no duplicates |
| `07_Safeguarding` | Crisis routing, housing urgency |
| `08_SpecialFlows` | Bin lookup flow, appointment state |
| `09_Resilience` | Typos, all-caps, empty input |
| `10_Regression` | Named regressions R1–R12 |
| `11_ApiContract` | Response shape, status codes |
| `12_Performance` | Guard latency under 100 ms |

### Python eval tests (requires running server)

```bash
cd Tests/eval
pip install -r requirements.txt

# Run against local server
pytest . -v --timeout=60

# Run against EC2
CHATBOT_BASE_URL=http://YOUR_EC2_IP:8080 pytest . -v --timeout=60
```

### Standalone evaluator (pass/fail table + CSV)

```bash
cd Tests/eval
python evaluate_responses.py

# Against a specific server
python evaluate_responses.py --url http://YOUR_EC2_IP:8080

# Only routing tests
python evaluate_responses.py --category routing

# Stop on first failure
python evaluate_responses.py --fail-fast
```

---

## Configuration

`appsettings.json` controls all tunable parameters:

```json
{
  "EmbeddingService": { "BaseUrl": "http://127.0.0.1:8001" },
  "LangChain":        { "BaseUrl": "http://127.0.0.1:8010" },
  "OpenAI": {
    "ApiKey":           "",
    "ChatModel":        "gpt-4o-mini",
    "EmbeddingModel":   "text-embedding-3-small"
  },
  "Retrieval": {
    "Threshold": 0.50
  },
  "Playwright": {
    "TargetUrl":          "https://onlineforms.bradford.gov.uk/ufs/collectiondates.eb",
    "Headless":           true,
    "WaitAfterSubmitMs":  2500,
    "TimeoutMs":          30000
  }
}
```

All values can be overridden by environment variables using the ASP.NET Core double-underscore convention: `OpenAI__ChatModel=gpt-4o`.

---

## Adding or Updating FAQ Content

1. Edit `Data/faqs.json` — add or update FAQ entries (service, title, answer, nextStepsUrl).
2. Delete `Data/chunks.embeddings.json` to invalidate the embedding cache.
3. Restart the app. It will rebuild the embeddings on startup and save a new cache.

To update the LangChain RAG knowledge base (scraped Bradford Council pages):

```bash
cd LangChainService

# Re-scrape pages (requires internet access)
python scrape_bradford_targeted.py

# Rebuild the FAISS index
EMBEDDING_URL=http://localhost:8001/embed python ingest.py
```

Then rebuild and push the `langchainservice` Docker image.

---

## Security Notes

- Never commit `.env` to version control — it is listed in `.gitignore` and all three `.dockerignore` files
- Use `.env.example` as the template; copy it to `.env` and fill in real values
- The OpenAI API key is passed to containers at runtime via environment variables only — it is never baked into any image layer
- The `"default"` session fallback in `ChatController.cs` has been removed — each request without a session ID gets a fresh isolated GUID session

---

## License

This project is a prototype developed for Bradford Metropolitan District Council. All rights reserved.
