# Bradford Council Citizen Support Conversational AI Agent

This repository contains the Industrial AI Project (Group B) for developing a **task-oriented Conversational AI Agent** to support Bradford Council citizens in accessing public services and information.

## Project Overview
The goal of this project is to design and prototype a conversational agent that can:
- understand citizen queries (intent + entities),
- guide users through council service workflows,
- retrieve accurate service information from approved sources,
- handle ambiguous or unsupported queries using safe fallback behaviour.

## Scope (Initial Use Cases)
Examples of intended use cases include:
- Bin Collection 
- Council tax 
- School Admissions


## Repository Structure
- `src/` – application source code
- `tests/` – unit/integration tests
- `data/` – project data (non-sensitive only)
- `docs/` – documentation (architecture, research notes, meeting notes, sprint artefacts)

## Architecture (High-Level)
The system follows a modular conversational AI architecture:
1. **Pre-processing**
2. **NLU** (intent classification + entity extraction)
3. **Dialogue management** (state tracking, policy decisions, context handling)
4. **Knowledge retrieval** (approved sources / knowledge base)
5. **NLG** (controlled response generation)
6. **Fallback & guardrails** (safe handling of uncertainty and out-of-scope queries)

Architecture diagrams and design pipelines are stored in: `docs/architecture/`.

## Getting Started
### Prerequisites
--TO-DO

### Setup
--TO-DO

### Documentation
**Meeting notes**: docs/meetings/
**Sprint planning/reviews/retros**: docs/sprint/
**Research notes**: docs/research/

### Disclaimer
This repository is developed as part of an academic Industrial AI Project. Any council-related information used is for prototyping and evaluation purposes and must be verified against official sources in real deployment scenarios.
