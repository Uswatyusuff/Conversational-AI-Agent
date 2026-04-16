// SuggestionChipTests.cs
// ─────────────────────────────────────────────────────────────────────────────
// Category 6: Suggestion chip quality
//
// Tests that suggestion chips:
//  - Are full, meaningful phrases (not single words like "Pay" or "Apply")
//  - Include escape options after service answers (e.g. "I was asking about
//    something different")
//  - Do not trap the user in loops (same chip that produced the clarification)
//  - Are present after every response type
// ─────────────────────────────────────────────────────────────────────────────

using BradfordChatbot.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BradfordChatbot.Tests._06_SuggestionChips;

public class SuggestionChipTests : ChatTestBase
{
    public SuggestionChipTests() : base(new LangChainStubResponse
    {
        Answer  = "You can pay your Council Tax online or by direct debit.",
        Service = "Council Tax",
        Action  = "answer"
    }) { }

    // ── Chips are present after all response types ────────────────────────────

    [Fact]
    public async Task GreetingResponse_HasSuggestions()
    {
        var result = await Chat("hello");
        result.suggestions.Should().NotBeEmpty();
    }

    [Fact]
    public async Task VagueHelpResponse_HasSuggestions()
    {
        var result = await Chat("I need help");
        result.suggestions.Should().NotBeEmpty();
    }

    [Fact]
    public async Task MeaninglessInputResponse_HasSuggestions()
    {
        var result = await Chat("asdfgh");
        result.suggestions.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ServiceResponse_HasSuggestions()
    {
        var result = await Chat("How do I pay my council tax?");
        result.suggestions.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ContactUsResponse_HasSuggestions()
    {
        var result = await Chat("how can I contact the council?");
        result.suggestions.Should().NotBeEmpty();
    }

    // ── Chips are substantive (not single words) ──────────────────────────────

    [Fact]
    public async Task GreetingSuggestions_AreFullPhrases_NotSingleWords()
    {
        var result = await Chat("hello");
        AssertSuggestionsAreSubstantive(result.suggestions);
    }

    [Fact]
    public async Task ServiceSuggestions_AreFullPhrases()
    {
        var result = await Chat("How do I pay my council tax?");
        AssertSuggestionsAreSubstantive(result.suggestions);
    }

    [Fact]
    public async Task MeaninglessInputSuggestions_AreFullPhrases()
    {
        var result = await Chat("asdf");
        AssertSuggestionsAreSubstantive(result.suggestions);
    }

    // ── Chips do not trap the user in clarification loops ────────────────────

    [Fact]
    public async Task CouncilTaxClarificationChips_AreFullSentences()
    {
        // When council tax clarification fires, the chips should be full sentences
        // like "How do I pay my Council Tax online?" not just "Pay"
        LangChainHandler.SetResponse(
            """{"answer":"","service":"Council Tax","action":"clarify","needs_clarification":true,"tool_used":"","next_steps_url":""}""");

        var result = await Chat("council tax");
        AssertSuggestionsAreSubstantive(result.suggestions);

        // None of the chips should be identical to the query that just produced
        // the clarification — that would loop the user back to the same state
        result.suggestions.Should().NotContain(s =>
    s.Equals("council tax", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BenefitsClarificationChips_AreFullSentences()
    {
        LangChainHandler.SetResponse(
            """{"answer":"","service":"Benefits & Support","action":"clarify","needs_clarification":true,"tool_used":"","next_steps_url":""}""");

        var result = await Chat("benefits");
        AssertSuggestionsAreSubstantive(result.suggestions);
    }

    // ── Benefits answer chips include redirect options ────────────────────────

    [Fact]
    public async Task BenefitsAnswer_HasRedirectChip()
    {
        // After a benefits answer, there should be at least one chip allowing
        // the user to say "this isn't the benefit I meant"
        LangChainHandler.SetResponse(
            """{"answer":"You can apply for Universal Credit online at gov.uk.","service":"Benefits & Support","action":"answer","needs_clarification":false,"tool_used":"rag","next_steps_url":""}""");

        var result = await Chat("How do I apply for benefits?");

        // At least one suggestion should reference an alternative / redirect
        var hasRedirect = result.suggestions.Any(s =>
            s.ContainsAny(StringComparison.OrdinalIgnoreCase,
                "different benefit",
                "something else",
                "not the right benefit",
                "different question",
                "other benefit",
                "another benefit",
                "back to menu"));

        hasRedirect.Should().BeTrue();
    }

    // ── No duplicate chips ────────────────────────────────────────────────────

    [Theory]
    [InlineData("hello")]
    [InlineData("How do I pay my council tax?")]
    [InlineData("I need help")]
    [InlineData("asdf")]
    public async Task Suggestions_ContainNoDuplicates(string message)
    {
        var result = await Chat(message);
        var distinct = result.suggestions.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        distinct.Should().Be(result.suggestions.Count);
    }

    // ── No null or whitespace chips ────────────────────────────────────────────

    [Theory]
    [InlineData("hello")]
    [InlineData("When is my bin collection?")]
    [InlineData("asdfgh")]
    public async Task Suggestions_ContainNoNullOrEmptyStrings(string message)
    {
        var result = await Chat(message);
        AssertSuggestionsValid(result.suggestions);
    }
}

// ── Extension for string.ContainsAny ─────────────────────────────────────────
internal static class StringExtensions
{
    public static bool ContainsAny(this string source, StringComparison comparison, params string[] candidates)
        => candidates.Any(c => source.Contains(c, comparison));
}
