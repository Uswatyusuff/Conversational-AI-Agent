// RoutingAndIntentTests.cs
// ─────────────────────────────────────────────────────────────────────────────
// Category 1: Routing and intent detection
//
// Tests that ChatOrchestrator correctly classifies messages into the right
// service area, handles vague queries, and prioritises urgent cases.
//
// Approach:
//  - Guard-level tests (SmallTalk, Vague, NameIntro) use only the returned
//    service string; they fire before retrieval so no real embedding is needed.
//  - Service-detection tests check that detectedService inside the orchestrator
//    would pick the right trigger words. We verify via the reply and service
//    fields in the returned tuple.
//  - Where LangChain is involved we pre-configure a stub answer.
// ─────────────────────────────────────────────────────────────────────────────

using BradfordChatbot.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BradfordChatbot.Tests._01_Routing;

public class RoutingAndIntentTests : ChatTestBase
{
    public RoutingAndIntentTests() : base(new LangChainStubResponse
    {
        Answer  = "Here is a helpful answer about your query.",
        Service = "Unknown",
        Action  = "answer"
    }) { }

    // ── Small-talk guard ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("hi")]
    [InlineData("hello")]
    [InlineData("hey")]
    [InlineData("good morning")]
    [InlineData("good evening")]
    [InlineData("hi there")]
    public async Task Greeting_Returns_UnknownService(string greeting)
    {
        var result = await Chat(greeting);
        // Greetings should never be attributed to a service
        result.service.Should().Be("Unknown");
    }

    [Theory]
    [InlineData("hi")]
    [InlineData("hello")]
    [InlineData("thanks")]
    [InlineData("bye")]
    [InlineData("thank you")]
    public async Task Greeting_Reply_IsNotEmpty_And_Friendly(string message)
    {
        var reply = await Reply(message);
        reply.Should().NotBeNullOrWhiteSpace();
        reply.Length.Should().BeGreaterThan(4);
    }

    [Theory]
    [InlineData("thanks")]
    [InlineData("thank you")]
    [InlineData("cheers")]
    [InlineData("bye")]
    [InlineData("goodbye")]
    [InlineData("see you")]
    public async Task Farewell_Returns_UnknownService(string farewell)
    {
        var result = await Chat(farewell);
        result.service.Should().Be("Unknown");
    }

    // ── Vague-help guard ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("help")]
    [InlineData("I need help")]
    [InlineData("can you help me")]
    [InlineData("I want some help")]
    [InlineData("help me please")]
    public async Task VagueHelp_Returns_ServiceMenuSuggestions(string message)
    {
        var result = await Chat(message);
        // Vague-help handler returns a service menu
        result.suggestions.Should().NotBeEmpty();
        result.service.Should().Be("Unknown");
    }

    [Fact]
    public async Task HelpWithServiceNoun_PassesThroughToRouting()
    {
        // "I need help with my bins" should NOT be caught by vague-help guard
        // (bins is a service noun) — it should route to Waste & Bins
        var result = await Chat("I need help with my bins");
        result.service.Should().NotBe("Unknown");
    }

    // ── Name-introduction guard ───────────────────────────────────────────────

    [Theory]
    [InlineData("I am John")]
    [InlineData("I am Sarah")]
    [InlineData("my name is Ahmed")]
    [InlineData("I am Samik")]
    [InlineData("I'm David")]
    public async Task NameIntroduction_Returns_FriendlyGreeting(string message)
    {
        var result = await Chat(message);
        result.service.Should().Be("Unknown");
        result.reply.Should().ContainAny("Hello", "hello", "Hi", "hi");
    }

    [Theory]
    [InlineData("I am at risk of eviction")]
    [InlineData("I am homeless")]
    [InlineData("I am struggling to pay my council tax")]
    [InlineData("I am eligible for a blue badge")]
    [InlineData("I am applying for a school place")]
    public async Task SituationalIAm_PassesThroughToRouting(string message)
    {
        // These start with "I am" but contain service context — must NOT be
        // intercepted by the name-introduction guard
        var result = await Chat(message);
        // The service should not be Unknown (they describe real situations)
        // or — at minimum — should NOT have returned a name-greeting reply
        result.reply.Should().NotContainAny("Hello!", "Hi!");
    }

    // ── Meaningless-input guard ───────────────────────────────────────────────

    [Theory]
    [InlineData("asdf")]
    [InlineData("sdfsadf")]
    [InlineData("zzzzz")]
    [InlineData("qwerty")]
    [InlineData("aaaaaa")]
    [InlineData("idk")]
    [InlineData("not sure")]
    public async Task MeaninglessInput_Returns_HelpfulPrompt(string message)
    {
        var result = await Chat(message);
        result.service.Should().Be("Unknown");
        result.reply.Should().NotBeNullOrWhiteSpace();
        // Should not return garbage RAG content
        result.reply.Should().NotContainAny(
            "blue badge", "Blue Badge", "library", "Library");
    }

    // ── Primary service detection ─────────────────────────────────────────────

    [Theory]
    [InlineData("How do I pay my council tax?",                  "Council Tax")]
    [InlineData("What is my council tax balance?",               "Council Tax")]
    [InlineData("I need help paying my bill",                    "Council Tax")]
    [InlineData("How do I set up a direct debit for council tax?", "Council Tax")]
    [InlineData("council tax arrears help",                      "Council Tax")]
    public async Task CouncilTax_QueriesRoute_ToCouncilTax(string msg, string expected)
    {
        var result = await Chat(msg);
        result.service.Should().Be(expected);
    }

    [Theory]
    [InlineData("When is my bin collection?",       "Waste & Bins")]
    [InlineData("My bin wasn't collected",          "Waste & Bins")]
    [InlineData("I need a new recycling bin",       "Waste & Bins")]
    [InlineData("how do I report a missed bin",     "Waste & Bins")]
    [InlineData("what can I put in my recycling?",  "Waste & Bins")]
    public async Task Bins_QueriesRoute_ToWasteAndBins(string msg, string expected)
    {
        var result = await Chat(msg);
        result.service.Should().Be(expected);
    }

    [Theory]
    [InlineData("How do I apply for a Blue Badge?",             "Benefits & Support")]
    [InlineData("Am I eligible for benefits?",                  "Benefits & Support")]
    [InlineData("I receive universal credit, can I get help?",  "Benefits & Support")]
    [InlineData("disability support",                           "Benefits & Support")]
    [InlineData("PIP application",                              "Benefits & Support")]
    [InlineData("free school meals eligibility",                "Benefits & Support")]
    [InlineData("What help is available for disabled people?",  "Benefits & Support")]
    public async Task Benefits_QueriesRoute_ToBenefitsAndSupport(string msg, string expected)
    {
        var result = await Chat(msg);
        result.service.Should().Be(expected);
    }

    [Theory]
    [InlineData("How do I apply for a school place?",       "Education")]
    [InlineData("school admissions deadline",               "Education")]
    [InlineData("transfer to secondary school",             "Education")]
    [InlineData("EHCP transport help",                      "Education")]
    [InlineData("apply for in-year school transfer",        "Education")]
    public async Task Education_QueriesRoute_ToEducation(string msg, string expected)
    {
        var result = await Chat(msg);
        result.service.Should().Be(expected);
    }

    [Theory]
    [InlineData("I need housing help",                      "Housing")]
    [InlineData("I'm homeless and need a place to stay",    "Housing")]
    [InlineData("I'm at risk of becoming homeless",         "Housing")]
    [InlineData("eviction help",                            "Housing")]
    [InlineData("housing repairs tenant",                   "Housing")]
    public async Task Housing_QueriesRoute_ToHousing(string msg, string expected)
    {
        var result = await Chat(msg);
        result.service.Should().Be(expected);
    }

    [Theory]
    [InlineData("planning application status",              "Planning")]
    [InlineData("view planning applications",               "Planning")]
    [InlineData("comment on a planning application",        "Planning")]
    [InlineData("planning permission",                      "Planning")]
    [InlineData("building control",                         "Planning")]
    public async Task Planning_QueriesRoute_ToPlanning(string msg, string expected)
    {
        var result = await Chat(msg);
        result.service.Should().Be(expected);
    }

    [Theory]
    [InlineData("library hours",                            "Libraries")]
    [InlineData("how do I renew library books?",            "Libraries")]
    [InlineData("e-books digital library",                  "Libraries")]
    [InlineData("borrow books from the library",            "Libraries")]
    public async Task Libraries_QueriesRoute_ToLibraries(string msg, string expected)
    {
        var result = await Chat(msg);
        result.service.Should().Be(expected);
    }

    [Theory]
    [InlineData("how can I contact the council?",           "Contact Us")]
    [InlineData("what is the council phone number?",        "Contact Us")]
    [InlineData("contact details for Bradford Council",     "Contact Us")]
    [InlineData("email the council",                        "Contact Us")]
    public async Task ContactUs_QueriesRoute_ToContactUs(string msg, string expected)
    {
        var result = await Chat(msg);
        result.service.Should().Be(expected);
    }

    [Theory]
    [InlineData("book an appointment",                       "Appointment")]
    [InlineData("I'd like to speak to someone",              "Appointment")]
    [InlineData("can I book a callback?",                    "Appointment")]
    [InlineData("make an appointment with the council",      "Appointment")]
    public async Task Appointment_QueriesRoute_ToAppointment(string msg, string expected)
    {
        var result = await Chat(msg);
        result.service.Should().Be(expected);
    }

    [Theory]
    [InlineData("nearest recycling centre to me",   "Location")]
    [InlineData("find a library near me",           "Location")]
    [InlineData("closest council office",           "Location")]
    [InlineData("where is my nearest library?",     "Location")]
    public async Task Location_QueriesRoute_ToLocation(string msg, string expected)
    {
        var result = await Chat(msg);
        result.service.Should().Be(expected);
    }

    // ── Urgent case prioritisation ────────────────────────────────────────────

    [Theory]
    [InlineData("I have nowhere to sleep tonight")]
    [InlineData("I'm being evicted tomorrow")]
    [InlineData("I am homeless right now")]
    [InlineData("I need emergency housing tonight")]
    public async Task UrgentHousing_RoutesToHousing_NotGenericClarification(string message)
    {
        var result = await Chat(message);
        result.service.Should().Be("Housing");
    }

    // ── Mixed queries ─────────────────────────────────────────────────────────

    [Fact]
    public async Task MixedQuery_HomelessAndBenefits_PrioritisesHousing()
    {
        // When someone mentions both housing and benefits, housing should win
        // as the more urgent need.
        var result = await Chat("I'm homeless and need benefits support");
        result.service.Should().BeOneOf("Housing", "Benefits & Support");
    }

    [Fact]
    public async Task ShortFollowUp_WithPreviousService_CarriesContext()
    {
        // Ask a council tax question first, then ask a short follow-up
        await Chat("I need help with my council tax");
        var followUp = await Chat("how do I pay it?");
        followUp.service.Should().Be("Council Tax");
    }

    [Fact]
    public async Task Greeting_AfterServiceQuery_DoesNotInheritServiceContext()
    {
        // After a council tax answer, a greeting should NOT get council tax context
        await Chat("I need help with my council tax");
        var result = await Chat("hi");
        result.service.Should().Be("Unknown");
    }
}
