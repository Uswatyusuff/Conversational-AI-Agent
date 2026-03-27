from typing import List, Optional
from pydantic import BaseModel, Field

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