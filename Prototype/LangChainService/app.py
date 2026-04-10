import os
from fastapi import FastAPI
from pydantic import BaseModel, Field
from typing import List

from agent import run_agent

app = FastAPI(title="LangChain Agent Service")


class ContextChunk(BaseModel):
    title: str = ""
    text: str = ""
    nextUrl: str = ""


class HistoryItem(BaseModel):
    role: str = "user"
    message: str = ""


class AgentRequest(BaseModel):
    question: str
    service_hint: str = "Unknown"
    context_chunks: List[ContextChunk] = Field(default_factory=list)
    history: List[HistoryItem] = Field(default_factory=list)


class AgentResponse(BaseModel):
    answer: str
    service: str = "Unknown"
    action: str = "answer"
    needs_clarification: bool = False
    tool_used: str = ""
    next_steps_url: str = ""


@app.get("/health")
def health():
    return {
        "status": "ok",
        "model": os.getenv("OPENAI_MODEL", "gpt-4o-mini")
    }


@app.post("/agent", response_model=AgentResponse)
def agent_endpoint(req: AgentRequest):
    result = run_agent(req)
    return AgentResponse(**result)