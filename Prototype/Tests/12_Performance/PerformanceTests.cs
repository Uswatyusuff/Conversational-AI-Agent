// PerformanceTests.cs
// ─────────────────────────────────────────────────────────────────────────────
// Category 12: Basic performance / non-functional tests
//
// Tests verify that:
//  - Guard-level responses (small-talk, meaningless, vague, name) respond in
//    under 100ms (they fire before any HTTP call)
//  - Service-detected responses (which call embedding + possibly LangChain)
//    respond in under 3s with stub services
//  - Multiple sequential requests don't degrade in response time
//
// These are baseline expectations with stub services (no real HTTP latency).
// In production, allow significantly more time (~2-5s per response).
//
// Assumption: Stub HTTP handlers respond instantly (no real network).
// ─────────────────────────────────────────────────────────────────────────────

using System.Diagnostics;
using BradfordChatbot.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BradfordChatbot.Tests._12_Performance;

public class PerformanceTests : ChatTestBase
{
    public PerformanceTests() : base(new LangChainStubResponse
    {
        Answer  = "Here is a response about your query.",
        Service = "Council Tax",
        Action  = "answer"
    }) { }

    // ── Guard-level handlers are fast (< 100ms) ──────────────────────────────
    // These guards fire BEFORE any embedding or LangChain call, so they should
    // be near-instant even in production.

    [Theory]
    [InlineData("hi")]
    [InlineData("hello")]
    [InlineData("thanks")]
    public async Task Greeting_RespondsWithin_100ms(string greeting)
    {
        var sw = Stopwatch.StartNew();
        await Chat(greeting);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(100);
    }

    [Theory]
    [InlineData("asdf")]
    [InlineData("sdfsadf")]
    [InlineData("zzzzzz")]
    public async Task MeaninglessInput_RespondsWithin_100ms(string mash)
    {
        var sw = Stopwatch.StartNew();
        await Chat(mash);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(100);
    }

    [Fact]
    public async Task VagueHelp_RespondsWithin_100ms()
    {
        var sw = Stopwatch.StartNew();
        await Chat("I need help");
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(100);
    }

    [Theory]
    [InlineData("I am John")]
    [InlineData("my name is Sarah")]
    public async Task NameIntroduction_RespondsWithin_100ms(string nameMsg)
    {
        var sw = Stopwatch.StartNew();
        await Chat(nameMsg);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(100);
    }

    // ── Service routing with stubs is fast (< 3s) ────────────────────────────
    // These paths include an embedding call + retrieval + possibly LangChain,
    // but with stub HTTP handlers there's no real network latency.

    [Theory]
    [InlineData("How do I pay my council tax?")]
    [InlineData("When is my bin collection?")]
    [InlineData("Am I eligible for a blue badge?")]
    public async Task ServiceQuery_RespondsWithin_3s_WithStubs(string message)
    {
        var sw = Stopwatch.StartNew();
        await Chat(message);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(3000);
    }

    // ── Sequential requests don't degrade ─────────────────────────────────────

    [Fact]
    public async Task TenSequentialGreetings_AverageUnder_50ms_Each()
    {
        var times = new List<long>();

        for (var i = 0; i < 10; i++)
        {
            var sw = Stopwatch.StartNew();
            await Chat("hello");
            sw.Stop();
            times.Add(sw.ElapsedMilliseconds);
        }

        var average = times.Average();
        average.Should().BeLessThan(50);
    }

    [Fact]
    public async Task FiveSequentialServiceQueries_AverageUnder_1000ms_Each()
    {
        var times = new List<long>();

        for (var i = 0; i < 5; i++)
        {
            var sw = Stopwatch.StartNew();
            await Chat("How do I pay my council tax?");
            sw.Stop();
            times.Add(sw.ElapsedMilliseconds);
        }

        var average = times.Average();
        average.Should().BeLessThan(1000);
    }

    // ── Memory operations are fast ────────────────────────────────────────────

    [Fact]
    public void ConversationMemory_GetAndSet_IsUnder_5ms()
    {
        var sw = Stopwatch.StartNew();

        for (var i = 0; i < 100; i++)
        {
            Memory.SetLastService(Session, "Council Tax");
            Memory.GetLastService(Session);
            Memory.AddTurn(Session, "user", $"Message {i}");
            Memory.GetRecentTurns(Session, 6);
        }

        sw.Stop();

        // 100 iterations of get/set/add/get should complete in under 50ms
        sw.ElapsedMilliseconds.Should().BeLessThan(50);
    }

    // ── Retrieval service is fast ─────────────────────────────────────────────

    [Fact]
    public void RetrievalService_TopK_IsUnder_5ms_For_12Chunks()
    {
        var retrieval = MockFactory.CreateRetrievalService();

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 1000; i++)
        {
            retrieval.TopK(TestFixtures.CouncilTaxVector, 3);
        }
        sw.Stop();

        // 1000 TopK calls on 12 chunks should complete in well under 100ms
        sw.ElapsedMilliseconds.Should().BeLessThan(100);
    }
}
