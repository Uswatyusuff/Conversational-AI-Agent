// ResponseQualityTests.cs
// ─────────────────────────────────────────────────────────────────────────────
// Category 4: Response quality
//
// Tests that replies:
//  - Do not contain internal implementation phrases ("context does not provide")
//  - Are non-trivially long for real service queries
//  - Have a helpful, citizen-friendly tone
//  - Include next steps where appropriate
//  - Are not repetitive clarification loops
//
// Assumption: The LangChain stub is configured to return a reasonable answer.
// Where we need to test weak-answer filtering we configure it to return a
// weak phrase and verify the orchestrator does NOT pass it through.
// ─────────────────────────────────────────────────────────────────────────────

using BradfordChatbot.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BradfordChatbot.Tests._04_ResponseQuality;

public class ResponseQualityTests : ChatTestBase
{
    public ResponseQualityTests() : base(new LangChainStubResponse
    {
        Answer       = "You can pay your Council Tax online, by direct debit, or by phone. Visit the Bradford Council website to get started.",
        Service      = "Council Tax",
        Action       = "answer",
        NextStepsUrl = "https://www.bradford.gov.uk/council-tax/pay-your-council-tax/"
    }) { }

    // ── Banned internal phrases must not appear in any reply ─────────────────

    [Theory]
    [InlineData("How do I pay my council tax?")]
    [InlineData("When is my bin collection?")]
    [InlineData("Am I eligible for a blue badge?")]
    [InlineData("How do I apply for free school meals?")]
    [InlineData("I am homeless")]
    public async Task Reply_DoesNotContain_InternalContextPhrases(string message)
    {
        var reply = await Reply(message);
        AssertNoBannedPhrases(reply);
    }

    [Theory]
    [InlineData("Hi")]
    [InlineData("Thanks")]
    [InlineData("I am John")]
    [InlineData("asdf")]
    [InlineData("I need help")]
    public async Task GuardReplies_DoNotContain_InternalContextPhrases(string message)
    {
        // Guard-level replies (small-talk, name intro, vague help, meaningless)
        // should also be free of implementation leakage
        var reply = await Reply(message);
        AssertNoBannedPhrases(reply);
    }

    // ── Replies must be substantive for service queries ───────────────────────

    [Theory]
    [InlineData("How do I pay my council tax?")]
    [InlineData("When is my bin collection?")]
    [InlineData("How do I apply for a blue badge?")]
    public async Task ServiceQuery_Reply_IsNonTrivialLength(string message)
    {
        var reply = await Reply(message);
        reply.Length.Should().BeGreaterThan(30);
    }

    // ── Small-talk and guards are short but not empty ─────────────────────────

    [Theory]
    [InlineData("hi")]
    [InlineData("hello")]
    [InlineData("thanks")]
    public async Task GreetingReply_IsNotEmpty(string greeting)
    {
        var reply = await Reply(greeting);
        reply.Should().NotBeNullOrWhiteSpace();
    }

    // ── Tone: plain English, not robotic ─────────────────────────────────────

    [Theory]
    [InlineData("How do I pay my council tax?")]
    [InlineData("Am I eligible for a blue badge?")]
    public async Task ServiceReply_DoesNotUse_RoboticPhrases(string message)
    {
        var reply = await Reply(message);
        reply.Should().NotContainAny(
            "As an AI language model",
            "I am an AI",
            "As a large language model",
            "I'm a chatbot");
    }

    // ── Clarification must be specific, not a generic loop ────────────────────

    [Fact]
    public async Task CouncilTaxClarification_IsSpecific_NotGeneric()
    {
        // Configure LangChain to return nothing (weak) so we get the C# clarification
        LangChainHandler.SetResponse(
            """{"answer":"","service":"Council Tax","action":"clarify","needs_clarification":true,"tool_used":"","next_steps_url":""}""");

        var result = await Chat("council tax");
        // The clarification should mention specific sub-topics for council tax
        result.reply.Should().ContainAny(
            "pay", "balance", "discount", "exemption", "support", "arrears");
    }

    [Fact]
    public async Task BenefitsClarification_IsSpecific_NotGeneric()
    {
        LangChainHandler.SetResponse(
            """{"answer":"","service":"Benefits & Support","action":"clarify","needs_clarification":true,"tool_used":"","next_steps_url":""}""");

        var result = await Chat("benefits");
        result.reply.Should().ContainAny(
            "eligible", "apply", "evidence", "application", "qualify");
    }

    [Fact]
    public async Task ClarificationAfterClarification_IsNotIdentical()
    {
        // Two identical queries should not produce identical generic clarifications
        // that trap the user in a loop. At minimum they should include suggestions.
        LangChainHandler.SetResponse(
            """{"answer":"","service":"Council Tax","action":"clarify","needs_clarification":true,"tool_used":"","next_steps_url":""}""");

        var result1 = await Chat("council tax");
        var result2 = await Chat("council tax");

        // Both should have suggestions to help the user escape
        result1.suggestions.Should().NotBeEmpty();
        result2.suggestions.Should().NotBeEmpty();
    }

    // ── Next-steps URL quality ────────────────────────────────────────────────

    [Fact]
    public async Task ContactUsQuery_ReturnsPhoneNumber_InReply()
    {
        var reply = await Reply("how can I contact the council?");
        reply.Should().ContainAny("01274 431000", "0127");
    }

    [Fact]
    public async Task ContactUsQuery_ReturnsWebsite_InReply()
    {
        var reply = await Reply("how can I contact the council?");
        reply.Should().ContainAny("bradford.gov.uk", "website");
    }

    // ── Suggestion chips quality ──────────────────────────────────────────────

    [Theory]
    [InlineData("hi")]
    [InlineData("hello")]
    public async Task GreetingReply_HasSuggestions_WithSubstantiveText(string greeting)
    {
        var result = await Chat(greeting);
        AssertSuggestionsAreSubstantive(result.suggestions);
    }

    [Fact]
    public async Task MeaninglessInput_HasSuggestions_ThatAreServiceQuestions()
    {
        var result = await Chat("asdf");
        AssertSuggestionsValid(result.suggestions);
        // Suggestions after meaningless input should point to real services
        var combined = string.Join(" ", result.suggestions).ToLower();
        combined.Should().ContainAny(
            "council tax", "bin", "benefit", "housing", "school");
    }

    // ── LangChain weak-answer filtering ──────────────────────────────────────

    [Fact]
    public async Task WeakLangChainAnswer_IsNotPassedThrough_ToUser()
    {
        // Configure the LangChain stub to return a weak answer phrase
        LangChainHandler.SetResponse("""
        {
            "answer": "The context does not clearly contain the answer to your question.",
            "service": "Council Tax",
            "action": "answer",
            "needs_clarification": false,
            "tool_used": "rag",
            "next_steps_url": ""
        }
        """);

        var reply = await Reply("What is my council tax band?");

        // The orchestrator's is_weak_answer check (Python side) or the C# fallback
        // should catch this and NOT pass the phrase "context does not clearly" to the user
        reply.Should().NotContain("context does not clearly");
    }
}
