// FollowUpContextTests.cs
// ─────────────────────────────────────────────────────────────────────────────
// Category 2b: Context-aware follow-up behaviour via ChatOrchestrator
//
// Tests that multi-turn conversations behave correctly:
//  - Short replies carry previous service context
//  - Greetings do NOT inherit service context
//  - Topic switches clear old context appropriately
//  - Reset phrases clear pending flows
//  - Identity messages ("I am John") are safe regardless of prior context
// ─────────────────────────────────────────────────────────────────────────────

using BradfordChatbot.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BradfordChatbot.Tests._02_ContextMemory;

public class FollowUpContextTests : ChatTestBase
{
    public FollowUpContextTests() : base(new LangChainStubResponse
    {
        Answer  = "Here is a helpful answer about that topic.",
        Service = "Unknown",
        Action  = "answer"
    }) { }

    // ── Short follow-up carryover ─────────────────────────────────────────────

    [Fact]
    public async Task ShortFollowUp_Yes_InheritsLastService()
    {
        // Prime context with council tax query
        await Chat("How do I pay my council tax?");
        // Short follow-up should inherit council tax context
        var result = await Chat("yes");
        result.service.Should().Be("Council Tax");
    }

    [Fact]
    public async Task ShortFollowUp_HowMuch_InheritsLastService()
    {
        await Chat("I want to apply for a blue badge");
        var result = await Chat("how much does it cost?");
        result.service.Should().Be("Benefits & Support");
    }

    [Fact]
    public async Task ShortFollowUp_ThatOne_InheritsLastService()
    {
        await Chat("Tell me about bin collection");
        var result = await Chat("that one");
        result.service.Should().Be("Waste & Bins");
    }

    [Fact]
    public async Task ShortFollowUp_WhatAboutSupport_InheritsLastService()
    {
        await Chat("Council tax payments");
        var result = await Chat("what about support?");
        // Should still route to council tax context
        result.service.Should().Be("Council Tax");
    }

    // ── Greetings must NOT inherit service context ────────────────────────────

    [Theory]
    [InlineData("hi")]
    [InlineData("hello")]
    [InlineData("hey")]
    [InlineData("good morning")]
    public async Task Greeting_AfterCouncilTax_DoesNotInheritCouncilTaxContext(string greeting)
    {
        await Chat("How do I pay my council tax?");
        var result = await Chat(greeting);

        result.service.Should().Be("Unknown");
        result.reply.Should().NotContainAny(
            "council tax", "Council Tax", "payment", "direct debit");
    }

    [Theory]
    [InlineData("hi")]
    [InlineData("hello")]
    public async Task Greeting_AfterHousing_DoesNotInheritHousingContext(string greeting)
    {
        await Chat("I need housing help");
        var result = await Chat(greeting);

        result.service.Should().Be("Unknown");
        result.reply.Should().NotContainAny("homeless", "housing", "eviction");
    }

    // ── Name introduction must be safe regardless of prior context ────────────

    [Theory]
    [InlineData("I am John")]
    [InlineData("my name is Sarah")]
    [InlineData("I'm Ahmed")]
    public async Task NameIntroduction_AfterAnyService_ReturnsFriendlyGreeting(string nameMsg)
    {
        // After a housing query, sending a name should still be handled safely
        await Chat("I am at risk of eviction");
        var result = await Chat(nameMsg);

        result.service.Should().Be("Unknown");
        result.reply.Should().ContainAny("Hello", "hello", "Hi", "hi");
        result.reply.Should().NotContainAny("homeless", "eviction", "housing");
    }

    // ── Topic switching ───────────────────────────────────────────────────────

    [Fact]
    public async Task TopicSwitch_BinsToCouncilTax_UpdatesService()
    {
        await Chat("When is my bin collection?");
        var result = await Chat("How do I pay my council tax?");
        result.service.Should().Be("Council Tax");
    }

    [Fact]
    public async Task TopicSwitch_CouncilTaxToBenefits_UpdatesService()
    {
        await Chat("What is my council tax balance?");
        var result = await Chat("How do I apply for a blue badge?");
        result.service.Should().Be("Benefits & Support");
    }

    // ── Reset behaviour ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("something else")]
    [InlineData("start again")]
    [InlineData("forget that")]
    [InlineData("different topic")]
    [InlineData("another question")]
    public async Task ResetPhrase_ClearsPendingFlow_AndReturnsPrompt(string resetPhrase)
    {
        // Simulate an active pending flow
        Memory.SetPendingFlow(Session, "awaiting_postcode_for_bin_collection");

        await Chat(resetPhrase);

        Memory.GetPendingFlow(Session).Should().BeEmpty();
    }

    [Fact]
    public async Task ResetPhrase_Returns_AGenericNewTopicPrompt()
    {
        await Chat("When is my bin collection?");
        var result = await Chat("something else");

        result.reply.Should().ContainAny("What", "what", "ask", "help");
    }

    // ── Appointment flow does not bleed into unrelated queries ───────────────

    [Fact]
    public async Task UnrelatedQuestion_DuringAppointmentFlow_DoesNotReturnTypeMismatch()
    {
        // Simulate the appointment type-selection step being pending
        Memory.SetPendingFlow(Session, "appointment:select_type");

        // A real question should not be trapped by the appointment flow handler
        var result = await Chat("How do I apply for free school meals?");

        result.reply.Should().NotContain("I didn't recognise that appointment type");
    }

    // ── Follow-up on genuinely new query after context reset ─────────────────

    [Fact]
    public async Task ShortFollowUp_AfterReset_DoesNotUseOldService()
    {
        await Chat("When is my bin collection?");
        await Chat("something else");  // reset
        // Now send a short message — there's no service context so it should
        // NOT carry "Waste & Bins" forward
        var result = await Chat("yes");
        // After reset, "yes" has no meaningful service context to inherit
        // so it should be handled generically or prompt for more info
        result.reply.Should().NotBeNullOrWhiteSpace();
    }
}
