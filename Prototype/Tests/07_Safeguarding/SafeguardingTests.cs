// SafeguardingTests.cs
// ─────────────────────────────────────────────────────────────────────────────
// Category 7: Safeguarding and urgent flows
//
// Tests that messages describing urgent situations (homelessness, domestic
// abuse risk, rough sleeping, eviction) are:
//  1. Routed immediately to Housing — not to generic clarification
//  2. Accompanied by replies that acknowledge urgency and provide actionable
//     contact information
//  3. Not trapped in low-priority flows (e.g. appointment booking loop)
//
// Assumption: LangChain stub returns a basic Housing answer. The key assertion
// is the service classification and that a contact number or urgent routing
// phrase is present.
// ─────────────────────────────────────────────────────────────────────────────

using BradfordChatbot.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BradfordChatbot.Tests._07_Safeguarding;

public class SafeguardingTests : ChatTestBase
{
    public SafeguardingTests() : base(new LangChainStubResponse
    {
        Answer  = "If you are homeless or at risk of losing your home, contact the Housing Options team immediately on 01274 431000. They can assess your situation and find emergency accommodation.",
        Service = "Housing",
        Action  = "answer"
    }) { }

    // ── Urgent messages route to Housing ─────────────────────────────────────

    [Theory]
    [InlineData("I am homeless")]
    [InlineData("I'm homeless and don't have anywhere to go")]
    [InlineData("I have nowhere to sleep tonight")]
    [InlineData("I am sleeping rough")]
    [InlineData("I am rough sleeping")]
    [InlineData("I'm living on the streets")]
    public async Task RoughSleeping_RoutesToHousing(string message)
    {
        var result = await Chat(message);
        result.service.Should().Be("Housing");
    }

    [Theory]
    [InlineData("I'm being evicted tomorrow")]
    [InlineData("I got an eviction notice")]
    [InlineData("my landlord is evicting me")]
    [InlineData("I am at risk of eviction")]
    [InlineData("I'm about to be made homeless")]
    public async Task EvictionRisk_RoutesToHousing(string message)
    {
        var result = await Chat(message);
        result.service.Should().Be("Housing");
    }

    [Theory]
    [InlineData("I need emergency housing tonight")]
    [InlineData("I need a place to stay urgently")]
    [InlineData("I need emergency accommodation")]
    public async Task EmergencyHousing_RoutesToHousing(string message)
    {
        var result = await Chat(message);
        result.service.Should().Be("Housing");
    }

    // ── Urgent replies include actionable contact info ─────────────────────────

    [Theory]
    [InlineData("I am homeless")]
    [InlineData("I'm being evicted")]
    [InlineData("I need emergency housing")]
    public async Task UrgentHousing_Reply_IncludesContactNumber(string message)
    {
        var reply = await Reply(message);
        reply.Should().ContainAny("01274 431000", "Housing Options", "contact");
    }

    [Theory]
    [InlineData("I am homeless")]
    [InlineData("I'm sleeping rough")]
    public async Task UrgentHousing_Reply_IsNotEmpty_OrGenericClarification(string message)
    {
        var result = await Chat(message);
        result.reply.Should().NotBeNullOrWhiteSpace();
        result.reply.Should().NotContainAny(
            "I didn't quite catch",
            "I'm not sure what you mean",
            "could you tell me a bit more");
    }

    // ── Urgent messages do not get trapped in appointment flow ────────────────

    [Fact]
    public async Task UrgentHousing_WhenAppointmentFlowActive_EscapesFlow()
    {
        // Simulate an active appointment pending flow
        Memory.SetPendingFlow(Session, "appointment:select_type");

        var result = await Chat("I am homeless and need help tonight");

        result.reply.Should().NotContain("I didn't recognise that appointment type");
        result.service.Should().Be("Housing");
    }

    // ── Domestic abuse wording ────────────────────────────────────────────────

    [Theory]
    [InlineData("I am fleeing domestic abuse")]
    [InlineData("I need to leave home because of domestic abuse")]
    [InlineData("I'm not safe at home")]
    public async Task DomesticAbuse_RoutesToHousing_OrBenefitsSupport(string message)
    {
        var result = await Chat(message);
        result.service.Should().BeOneOf("Housing", "Benefits & Support");
        result.reply.Should().NotContainAny(
            "I didn't quite catch",
            "I'm not sure what you mean");
    }

    // ── Vulnerable wording ────────────────────────────────────────────────────

    [Theory]
    [InlineData("I am vulnerable and need help with housing")]
    [InlineData("I'm a vulnerable adult who is homeless")]
    public async Task VulnerableUser_RoutesToHousing(string message)
    {
        var result = await Chat(message);
        result.service.Should().Be("Housing");
    }

    // ── No unnecessary safeguarding escalation for non-urgent queries ─────────

    [Theory]
    [InlineData("How do I renew my library books?")]
    [InlineData("When is my bin collection?")]
    [InlineData("I want to apply for a Blue Badge")]
    public async Task RoutineQuery_DoesNotTrigger_HousingRoute(string message)
    {
        var result = await Chat(message);
        result.service.Should().NotBe("Housing");
    }
}
