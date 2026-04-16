// RegressionTests.cs
// ─────────────────────────────────────────────────────────────────────────────
// Category 10: Regression tests for known historical failures
//
// Each test here documents a specific bug that occurred in a previous session
// and verifies it remains fixed. When adding a new regression test:
//  1. Add a comment explaining which bug it covers
//  2. Reference the session or date if known
//  3. Keep the test name descriptive
//
// KNOWN REGRESSIONS COVERED:
//  R1  – "hi" after a Council Tax answer → should NOT be classified as Council Tax
//  R2  – "idk" → should NOT return random Blue Badge content
//  R3  – "sdfsadf" → should NOT return random Library content
//  R4  – "I am [NAME]" → should NOT route to Housing
//  R5  – "How do I apply for council tax support?" → should NOT return student discount
//  R6  – "What help is available for disabled people?" → should NOT route to Council Tax
//  R7  – "How do I apply late?" in Education context → should NOT go to Benefits
//  R8  – "How can I contact the council?" → should NOT fall into generic clarification
//  R9  – "I am at risk of eviction" → should NOT be caught by name-introduction guard
//  R10 – Suggestion chips as single words → should NOT loop user back to clarification
//  R11 – Appointment type selection: "I am applying" during flow → should escape
//  R12 – "I need help paying bills" → should NOT be caught by vague-help guard
// ─────────────────────────────────────────────────────────────────────────────

using BradfordChatbot.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BradfordChatbot.Tests._10_Regression;

public class RegressionTests : ChatTestBase
{
    public RegressionTests() : base(new LangChainStubResponse
    {
        Answer  = "Here is the relevant information about your query.",
        Service = "Unknown",
        Action  = "answer"
    }) { }

    // ── R1: "hi" after Council Tax answer must not inherit Council Tax context ─

    [Fact]
    public async Task R1_Greeting_AfterCouncilTaxAnswer_DoesNotInheritCouncilTaxService()
    {
        // Bug: "hi" after a council-tax answer was being prepended with "council tax",
        // causing embedding retrieval to pull council-tax chunks into the greeting reply.
        await Chat("How do I pay my council tax?");
        var result = await Chat("hi");

        result.service.Should().Be("Unknown");
        result.reply.Should().NotContainAny(
            "council tax", "Council Tax", "payment", "direct debit");
    }

    // ── R2: "idk" must not return Blue Badge content ──────────────────────────

    [Fact]
    public async Task R2_Idk_DoesNotReturnBlueBadgeContent()
    {
        // Bug: "idk" was being embedded, and Blue Badge chunks happened to score
        // highest for that gibberish vector, returning parking badge fraud content.
        var result = await Chat("idk");

        result.reply.ToLower().Should().NotContainAny(
            "blue badge", "parking badge", "disabled badge");
        result.service.Should().Be("Unknown");
    }

    // ── R3: "sdfsadf" must not return Library content ─────────────────────────

    [Fact]
    public async Task R3_Gibberish_DoesNotReturnLibraryContent()
    {
        // Bug: "sdfsadf" was bypassing the meaningless-input guard and reaching
        // RAG retrieval, where Library chunks accidentally scored highest.
        var result = await Chat("sdfsadf");

        result.reply.ToLower().Should().NotContainAny(
            "library", "renew", "borrow books", "e-books");
        result.service.Should().Be("Unknown");
    }

    // ── R4: "I am [NAME]" must not route to Housing ───────────────────────────

    [Theory]
    [InlineData("I am John")]
    [InlineData("I am Sarah")]
    [InlineData("my name is Ahmed")]
    [InlineData("I am Samik")]
    public async Task R4_NameIntroduction_DoesNotRouteToHousing(string message)
    {
        // Bug: "I am John" was being embedded, and the word "am" / short phrase
        // caused Housing / homelessness chunks to rank highest.
        var result = await Chat(message);

        result.service.Should().Be("Unknown");
        result.service.Should().NotBe("Housing");
        result.reply.Should().ContainAny("Hello", "hello", "Hi", "hi");
    }

    // ── R5: "council tax support" must not return student discount ─────────────

    [Fact]
    public async Task R5_CouncilTaxSupport_DoesNotReturn_StudentDiscount()
    {
        // Bug: "How do I apply for council tax support?" was retrieving the
        // student exemption chunk (high cosine similarity) instead of the
        // Council Tax Support benefit page.
        LangChainHandler.SetResponse("""
        {
            "answer": "Council Tax Support helps people on low incomes pay their council tax. You can apply online at bradford.gov.uk.",
            "service": "Benefits & Support",
            "action": "answer",
            "needs_clarification": false,
            "tool_used": "rag",
            "next_steps_url": "https://www.bradford.gov.uk/benefits/council-tax-support/"
        }
        """);

        var result = await Chat("How do I apply for council tax support?");

        result.reply.Should().NotContain("student");
        result.reply.Should().NotContain("full-time");
    }

    // ── R6: "disabled people" query must not route to Council Tax ─────────────

    [Fact]
    public async Task R6_DisabledPeopleQuery_DoesNotRouteToCouncilTax()
    {
        // Bug: "What help is available for disabled people?" was routing to
        // Council Tax because "disability discount/exemption" is a council tax topic.
        // The question is primarily about benefits/support, not council tax.
        var result = await Chat("What help is available for disabled people?");

        result.service.Should().NotBe("Council Tax");
        result.service.Should().Be("Benefits & Support");
    }

    // ── R7: "How do I apply late?" in Education must not go to Benefits ────────

    [Fact]
    public async Task R7_LateApplicationInEducationContext_StaysInEducation()
    {
        // Bug: After an education question, asking "how do I apply late?" was
        // being caught by the Benefits trigger (the word "apply" + short message
        // pulled benefits chunks).
        await Chat("How do I apply for a school place?");
        var result = await Chat("how do I apply late?");

        result.service.Should().NotBe("Benefits & Support");
    }

    // ── R8: "How can I contact the council?" must not fall to clarification ────

    [Fact]
    public async Task R8_ContactCouncil_Returns_ContactDetails_NotClarification()
    {
        // Bug: "How can I contact the council?" was not hitting the IsContactUsIntent
        // handler and fell through to RAG, which returned generic clarification
        // because no specific answer chunk matched.
        var result = await Chat("How can I contact the council?");

        result.service.Should().Be("Contact Us");
        result.reply.Should().ContainAny("01274 431000", "bradford.gov.uk");
    }

    // ── R9: "I am at risk of eviction" must escape name-introduction guard ────

    [Fact]
    public async Task R9_IAmAtRiskOfEviction_IsNotCaughtByNameGuard()
    {
        // Bug: The IsIdentityOnlyInput regex matched "I am [anything]", catching
        // "I am at risk of eviction" before Housing routing could fire.
        var result = await Chat("I am at risk of eviction");

        result.reply.Should().NotContainAny("Hello", "Hi");
        result.service.Should().Be("Housing");
    }

    // ── R10: Suggestion chips must not cause clarification loops ──────────────

    [Fact]
    public async Task R10_ClarificationChips_AreFullSentences_Not_SingleWords()
    {
        // Bug: Suggestion chips after clarification were single words ("Pay", "Apply"),
        // which scored below threshold and looped the user back to the same clarification.
        LangChainHandler.SetResponse("""
        {
            "answer": "",
            "service": "Council Tax",
            "action": "clarify",
            "needs_clarification": true,
            "tool_used": "",
            "next_steps_url": ""
        }
        """);

        var result = await Chat("council tax");

        result.suggestions.Should().AllSatisfy(chip =>
            chip.Length.Should().BeGreaterThan(9));
    }

    // ── R11: "I am applying" during appointment type selection must escape ─────

    [Fact]
    public async Task R11_RealSentence_DuringAppointmentTypeSelect_EscapesFlow()
    {
        // Bug: "I am applying for universal credit" was matched by the appointment
        // type handler which returned "I didn't recognise that appointment type".
        Memory.SetPendingFlow(Session, "appointment:select_type");

        var result = await Chat("I am applying for universal credit");

        result.reply.Should().NotContain("I didn't recognise that appointment type");
    }

    // ── R12: "I need help paying bills" must not trigger vague-help guard ──────

    [Fact]
    public async Task R12_HelpPayingBills_IsNotCaughtByVagueHelpGuard()
    {
        // Bug: "I need help paying bills" contained the words "I need help" which
        // triggered the vague-help guard even though "bills" is a council-tax signal.
        var result = await Chat("I need help paying bills");

        result.service.Should().NotBe("Unknown");
        result.service.Should().Be("Council Tax");
    }
}
