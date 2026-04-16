// AccuracyGroundingTests.cs
// ─────────────────────────────────────────────────────────────────────────────
// Category 5: Accuracy and cross-service contamination prevention
//
// Tests that:
//  - Service-specific queries return answers relevant to the correct service
//  - Known contamination patterns are prevented (e.g. benefits chunk hijacking
//    a housing query, student discount surfacing for council tax support)
//  - Retrieval ranks correct chunks higher than incorrect ones
//
// Approach:
//  - We use the RetrievalService directly (unit tests) to verify ranking
//  - We use the orchestrator end-to-end to verify service boundaries
//
// The test chunks in TestFixtures.SampleChunks use orthogonal vectors so
// retrieval ranking is deterministic.
// ─────────────────────────────────────────────────────────────────────────────

using CouncilChatbotPrototype.Models;
using CouncilChatbotPrototype.Services;
using BradfordChatbot.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BradfordChatbot.Tests._05_Accuracy;

public class AccuracyGroundingTests
{
    private readonly RetrievalService _retrieval;

    public AccuracyGroundingTests()
    {
        _retrieval = MockFactory.CreateRetrievalService();
    }

    // ── Retrieval ranking: correct chunk wins ─────────────────────────────────

    [Fact]
    public void TopK_CouncilTaxVector_ReturnsBestCouncilTaxChunk()
    {
        var results = _retrieval.TopK(TestFixtures.CouncilTaxVector, 3);
        results.Should().NotBeEmpty();
        results[0].chunk.Service.Should().Be("Council Tax");
    }

    [Fact]
    public void TopK_WasteBinsVector_ReturnsBestWasteBinsChunk()
    {
        var results = _retrieval.TopK(TestFixtures.WasteBinsVector, 3);
        results[0].chunk.Service.Should().Be("Waste & Bins");
    }

    [Fact]
    public void TopK_BenefitsVector_ReturnsBestBenefitsChunk()
    {
        var results = _retrieval.TopK(TestFixtures.BenefitsVector, 3);
        results[0].chunk.Service.Should().Be("Benefits & Support");
    }

    [Fact]
    public void TopK_HousingVector_ReturnsBestHousingChunk()
    {
        var results = _retrieval.TopK(TestFixtures.HousingVector, 3);
        results[0].chunk.Service.Should().Be("Housing");
    }

    [Fact]
    public void TopK_EducationVector_ReturnsBestEducationChunk()
    {
        var results = _retrieval.TopK(TestFixtures.EducationVector, 3);
        results[0].chunk.Service.Should().Be("Education");
    }

    [Fact]
    public void TopK_PlanningVector_ReturnsBestPlanningChunk()
    {
        var results = _retrieval.TopK(TestFixtures.PlanningVector, 3);
        results[0].chunk.Service.Should().Be("Planning");
    }

    [Fact]
    public void TopK_LibrariesVector_ReturnsBestLibrariesChunk()
    {
        var results = _retrieval.TopK(TestFixtures.LibrariesVector, 3);
        results[0].chunk.Service.Should().Be("Libraries");
    }

    // ── TopKInService: service-scoped retrieval ───────────────────────────────

    [Fact]
    public void TopKInService_OnlyReturnsChunks_FromSpecifiedService()
    {
        var results = _retrieval.TopKInService(TestFixtures.CouncilTaxVector, "Council Tax", 5);
        results.Should().NotBeEmpty();
        results.Should().AllSatisfy(r =>
            r.chunk.Service.Should().Be("Council Tax"));
    }

    [Fact]
    public void TopKInService_WrongService_ReturnsEmptyOrOnlyMatchingChunks()
    {
        // Ask for "Education" chunks but pass a council-tax vector —
        // should still only return Education chunks (service filter is hard)
        var results = _retrieval.TopKInService(TestFixtures.CouncilTaxVector, "Education", 5);
        results.Should().AllSatisfy(r =>
            r.chunk.Service.Should().Be("Education"));
    }

    [Fact]
    public void TopK_ReturnsScores_WithinValidRange()
    {
        var results = _retrieval.TopK(TestFixtures.CouncilTaxVector, 5);
        results.Should().AllSatisfy(r =>
        {
            r.score.Should().BeGreaterThan(-1.01f, "cosine similarity floor is -1");
            r.score.Should().BeLessThan(1.01f,  "cosine similarity ceiling is 1");
        });
    }

    // ── Cross-service contamination prevention (orchestrator-level) ───────────

    [Theory(Skip = "Requires integration setup with real orchestrator + vector alignment")]
    [InlineData("What help is available for disabled people?",
                "Benefits & Support",
                "Council Tax",
                "a disability query should route to Benefits, not Council Tax")]
    [InlineData("I'm homeless and need a place to stay",
                "Housing",
                "Benefits & Support",
                "a homelessness query should route to Housing, not Benefits")]
    [InlineData("How do I apply for council tax support?",
                "Benefits & Support",
                "Council Tax",  // council tax support IS a benefits product
                "Council Tax Support is a benefit — should route to Benefits & Support")]
    public async Task ServiceQuery_DoesNotContaminate_WrongService(
        string query,
        string expectedService,
        string wrongService,
        string reason)
    {
        // NOTE: This test is marked Skip because it requires the real orchestrator
        // with real embeddings and real RAG content to be meaningful. Run it in the
        // integration environment (see README).
        var (orchestrator, _) = MockFactory.CreateOrchestrator();
        var result = await orchestrator.HandleChatAsync("cross-service-test", query);
        result.service.Should().Be(expectedService);
        result.service.Should().NotBe(wrongService);
    }

    // ── Council Tax Support ≠ student discount ────────────────────────────────

    [Fact]
    public async Task CouncilTaxSupport_Query_DoesNotMention_StudentDiscount()
    {
        // "Council Tax Support" is a low-income benefit, NOT a student discount.
        // The reply should not conflate the two.
        var (orchestrator, handler) = MockFactory.CreateOrchestrator(
            new LangChainStubResponse
            {
                Answer  = "Council Tax Support helps people on low incomes. You can apply online.",
                Service = "Benefits & Support",
                Action  = "answer"
            });

        var result = await orchestrator.HandleChatAsync("ctax-support-test",
            "How do I apply for council tax support?");

        result.reply.Should().NotContain("student");
        result.reply.Should().NotContain("full-time student");
    }

    // ── Housing query should not resolve to Housing Benefit ──────────────────

    [Fact]
    public async Task HomelessnessQuery_Reply_IsAboutHousingOptions_NotBenefit()
    {
        var (orchestrator, handler) = MockFactory.CreateOrchestrator(
            new LangChainStubResponse
            {
                Answer  = "If you are homeless or at risk of homelessness, contact the Housing Options team on 01274 431000.",
                Service = "Housing",
                Action  = "answer"
            });

        var result = await orchestrator.HandleChatAsync("housing-test",
            "I'm homeless and need emergency housing");

        result.service.Should().Be("Housing");
        // Should mention housing options/emergency help, not housing benefit payments
        result.reply.Should().NotContain("Housing Benefit");
    }

    // ── No invented URLs or phone numbers ────────────────────────────────────

    [Fact]
    public async Task CouncilTaxReply_IfContainsUrl_MustBeRealBradfordUrl()
    {
        var (orchestrator, _) = MockFactory.CreateOrchestrator(
            new LangChainStubResponse
            {
                Answer       = "Pay online at the Bradford Council website.",
                Service      = "Council Tax",
                Action       = "answer",
                NextStepsUrl = "https://www.bradford.gov.uk/council-tax/pay-your-council-tax/"
            });

        var result = await orchestrator.HandleChatAsync("url-test",
            "How do I pay my council tax?");

        if (!string.IsNullOrWhiteSpace(result.nextStepsUrl))
        {
            result.nextStepsUrl.Should().StartWith("https://www.bradford.gov.uk");
        }
    }
}
