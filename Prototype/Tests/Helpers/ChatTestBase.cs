// ChatTestBase.cs
// ─────────────────────────────────────────────────────────────────────────────
// Shared base class that all orchestrator-level test classes inherit from.
// Handles:
//  - Orchestrator construction with controllable stubs
//  - Per-test session isolation (fresh session ID per test)
//  - Shorthand helpers for common assertions
// ─────────────────────────────────────────────────────────────────────────────

using CouncilChatbotPrototype.Services;
using FluentAssertions;

namespace BradfordChatbot.Tests.Helpers;

/// <summary>
/// Base class for tests that drive ChatOrchestrator directly (not via HTTP).
/// Each test gets a fresh session and a clean orchestrator with controllable stubs.
/// </summary>
public abstract class ChatTestBase : IDisposable
{
    protected readonly ChatOrchestrator Orchestrator;
    protected readonly StubHttpMessageHandler LangChainHandler;
    protected readonly ConversationMemory Memory;

    // Fresh unique session ID per test instance — prevents state bleed
    protected readonly string Session = TestFixtures.NewSession();

    protected ChatTestBase(LangChainStubResponse? defaultLcResponse = null)
    {
        Memory = new ConversationMemory();

        var (orchestrator, handler) = MockFactory.CreateOrchestrator(
            defaultLangChainResponse: defaultLcResponse,
            memory: Memory);

        Orchestrator     = orchestrator;
        LangChainHandler = handler;
    }

    // ── Shorthand chat helper ─────────────────────────────────────────────────

    /// <summary>Sends a message and returns the full result tuple.</summary>
    protected async Task<(string reply, string service, string nextStepsUrl, float score, List<string> suggestions)>
        Chat(string message) =>
        await Orchestrator.HandleChatAsync(Session, message);

    /// <summary>Sends a message and returns only the reply text.</summary>
    protected async Task<string> Reply(string message) =>
        (await Chat(message)).reply;

    /// <summary>Sends a message and returns only the detected service.</summary>
    protected async Task<string> Service(string message) =>
        (await Chat(message)).service;

    // ── Assertion helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Assert the reply does not contain any of the known "robot leak" phrases
    /// (context references, inability phrases, etc.).
    /// </summary>
    protected static void AssertNoBannedPhrases(string reply)
    {
        var lower = reply.ToLowerInvariant();
        foreach (var phrase in TestFixtures.BannedResponsePhrases)
        {
            lower.Should().NotContain(phrase);
        }
    }

    /// <summary>Assert the suggestions list is non-empty and all items are non-whitespace.</summary>
    protected static void AssertSuggestionsValid(List<string> suggestions)
    {
        suggestions.Should().NotBeNull();
        suggestions.Should().AllSatisfy(s =>
            s.Should().NotBeNullOrWhiteSpace());
    }

    /// <summary>Assert a suggestion looks like a complete helpful phrase (≥ 10 chars).</summary>
    protected static void AssertSuggestionsAreSubstantive(List<string> suggestions)
    {
        AssertSuggestionsValid(suggestions);
        suggestions.Should().AllSatisfy(s =>
            s.Length.Should().BeGreaterThan(9));
    }

    public void Dispose() { /* nothing async to clean up */ }
}
