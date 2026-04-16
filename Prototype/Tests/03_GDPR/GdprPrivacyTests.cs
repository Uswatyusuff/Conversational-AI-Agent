// GdprPrivacyTests.cs
// ─────────────────────────────────────────────────────────────────────────────
// Category 3: GDPR and privacy compliance
//
// These tests verify that the chatbot:
//  1. Does not echo back full PII supplied by the user
//  2. Does not proactively solicit sensitive data (NI numbers, dates of birth)
//     unless it is operationally necessary (e.g. mid-form flow)
//  3. Sanitises PII before storing it in ConversationMemory
//  4. Only asks for postcodes inside the bin-collection / location lookup flows
//  5. Handles name introductions safely without repeating the name excessively
//
// Assumption: PII echoing is tested on the memory layer (direct) and on the
// reply text returned by the orchestrator (indirect). The orchestrator is not
// expected to actively echo back the raw user message verbatim in its reply.
// ─────────────────────────────────────────────────────────────────────────────

using CouncilChatbotPrototype.Services;
using BradfordChatbot.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BradfordChatbot.Tests._03_GDPR;

public class GdprPrivacyTests : ChatTestBase
{
    public GdprPrivacyTests() : base(new LangChainStubResponse
    {
        Answer  = "Here is some general information about council services.",
        Service = "Unknown",
        Action  = "answer"
    }) { }

    // ── Name handling ─────────────────────────────────────────────────────────

    [Fact]
    public async Task NameIntroduction_IsNotEchoedBackVerbatim_InReply()
    {
        // The system acknowledges the user but must not use their full name
        // in a way that leaks it into downstream logging or unexpected replies.
        var result = await Chat("I am John Smith");
        result.reply.Should().NotContain("John Smith");
    }

    [Fact]
    public async Task NameIntroduction_MayGreetWithFirstName_Only()
    {
        // Acceptable: "Hello John!" — but NOT "Hello John Smith!"
        var result = await Chat("I am John Smith");
        // The reply must not contain the full surname
        result.reply.Should().NotContain("Smith");
    }

    // ── Memory sanitisation ────────────────────────────────────────────────────

    [Fact]
    public void Memory_SanitisesPostcode_BeforeStorage()
    {
        Memory.AddTurn(Session, "user", "My postcode is BD3 8PX please help me");
        var turns = Memory.GetRecentTurns(Session, 1);
        turns.Single().Message.Should().NotContain("BD3 8PX");
        turns.Single().Message.Should().Contain("[POSTCODE]");
    }

    [Fact]
    public void Memory_SanitisesEmail_BeforeStorage()
    {
        Memory.AddTurn(Session, "user", "My email is resident@bradford.gov.uk");
        var turns = Memory.GetRecentTurns(Session, 1);
        turns.Single().Message.Should().NotContain("resident@bradford.gov.uk");
        turns.Single().Message.Should().Contain("[EMAIL]");
    }

    [Fact]
    public void Memory_SanitisesPhone_BeforeStorage()
    {
        Memory.AddTurn(Session, "user", "Please call me on 07700 900123");
        var turns = Memory.GetRecentTurns(Session, 1);
        turns.Single().Message.Should().NotContain("07700 900123");
        turns.Single().Message.Should().Contain("[PHONE]");
    }

    [Fact]
    public void Memory_SanitisesNameStatement_BeforeStorage()
    {
        Memory.AddTurn(Session, "user", "My name is Jane Doe");
        var turns = Memory.GetRecentTurns(Session, 1);
        turns.Single().Message.Should().NotContain("Jane Doe");
    }

    [Fact]
    public void MaskedPostcode_RetainsOnlyInwardCode()
    {
        Memory.SetMaskedPostcode(Session, "BD3 8PX");
        var masked = Memory.GetMaskedPostcode(Session);
        masked.Should().NotBe("BD3 8PX");
        masked.Should().StartWith("[POSTCODE:");
    }

    // ── No unprompted solicitation of sensitive data ─────────────────────────

    [Theory]
    [InlineData("How do I apply for a Blue Badge?")]
    [InlineData("I want to pay my council tax")]
    [InlineData("When is my bin collection?")]
    [InlineData("Tell me about free school meals")]
    public async Task GeneralServiceQuery_DoesNotAskForNationalInsuranceNumber(string message)
    {
        var reply = await Reply(message);
        reply.Should().NotContainAny(
            "national insurance", "NI number", "NIN", "NINO");
    }

    [Theory]
    [InlineData("How do I apply for a Blue Badge?")]
    [InlineData("What discounts are available for council tax?")]
    public async Task GeneralServiceQuery_DoesNotAskForDateOfBirth(string message)
    {
        var reply = await Reply(message);
        reply.Should().NotContainAny(
            "date of birth", "DOB", "your birthday");
    }

    // ── Postcode only requested in appropriate flows ──────────────────────────

    [Fact]
    public async Task CouncilTaxQuery_DoesNotAskForPostcode()
    {
        var reply = await Reply("How do I pay my council tax?");
        // A general council tax payment query should never ask for a postcode
        reply.Should().NotContain("postcode");
    }

    [Fact]
    public async Task BenefitsQuery_DoesNotAskForPostcode()
    {
        var reply = await Reply("Am I eligible for a blue badge?");
        reply.Should().NotContain("postcode");
    }

    [Fact]
    public async Task BinCollectionQuery_CanAskForPostcode()
    {
        // Bin collection legitimately requires a postcode — this is acceptable
        var reply = await Reply("When is my bin collection day?");
        // We don't assert it MUST ask for postcode (handled by Python agent),
        // just that the response is sensible and doesn't ask for unrelated data
        reply.Should().NotContainAny(
            "national insurance", "date of birth", "NI number");
    }

    // ── Postcode not echoed unsafely in replies ───────────────────────────────

    [Fact]
    public async Task PostcodeInQuery_IsNotEchoedInReply_Verbatim()
    {
        // If a user accidentally pastes their postcode in a non-bin query,
        // the orchestrator must not echo it back in the reply body.
        var result = await Chat("I live in BD3 8PX, how do I pay council tax?");

        // The reply should handle the council tax question but not reflect the postcode
        result.reply.Should().NotContain("BD3 8PX");
    }

    // ── Appointment flow: only collects what is needed ────────────────────────

    [Fact]
    public async Task AppointmentFlow_OnlyAsksForContactDetails_NotFullAddress()
    {
        // Appointment flow should ask for name/phone/email — not full home address
        var reply = await Reply("book an appointment");
        reply.Should().NotContainAny(
            "full address", "home address", "street address");
    }
}
