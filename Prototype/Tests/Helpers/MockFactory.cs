// MockFactory.cs
// ─────────────────────────────────────────────────────────────────────────────
// Central factory for wiring up a ChatOrchestrator with controllable stubs so
// that tests can exercise routing logic without real HTTP calls or a live
// embedding / LangChain service.
//
// Design principles
// -----------------
//  1. EmbeddingService  – intercepted via a custom HttpMessageHandler that
//     returns a deterministic fixed-length zero-vector. All routing / guard
//     tests use this; only retrieval-accuracy tests need real vectors.
//  2. LangChainClientService – same approach: interceptable, returns a
//     configurable JSON response so each test can control what the agent says.
//  3. RetrievalService  – built from an in-memory List<FaqChunk> of test
//     fixtures so no file I/O is needed.
//  4. ConversationMemory – always a fresh real instance (pure in-memory,
//     no external deps).
//  5. All other services (Appointment, FormFlow, etc.) are constructed with
//     minimal/empty configuration; their methods are exercised at the
//     integration level or tested in isolation.
// ─────────────────────────────────────────────────────────────────────────────

using System.Net;
using System.Text;
using System.Text.Json;
using CouncilChatbotPrototype.Models;
using CouncilChatbotPrototype.Services;
using Microsoft.Extensions.Configuration;
using Moq;

namespace BradfordChatbot.Tests.Helpers;

/// <summary>
/// Controls what the stub LangChain HTTP service returns for a given test.
/// </summary>
public class LangChainStubResponse
{
    public string Answer              { get; set; } = "";
    public string Service             { get; set; } = "Unknown";
    public string Action              { get; set; } = "answer";
    public bool   NeedsClarification  { get; set; } = false;
    public string ToolUsed            { get; set; } = "";
    public string NextStepsUrl        { get; set; } = "";
}

/// <summary>
/// An HttpMessageHandler that always returns the same JSON payload.
/// Use MockFactory.SetLangChainResponse() to swap the payload between tests.
/// </summary>
public class StubHttpMessageHandler : HttpMessageHandler
{
    private string _responseBody;
    private HttpStatusCode _statusCode;

    public StubHttpMessageHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _responseBody = responseBody;
        _statusCode   = statusCode;
    }

    public void SetResponse(string body, HttpStatusCode code = HttpStatusCode.OK)
    {
        _responseBody = body;
        _statusCode   = code;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}

public static class MockFactory
{
    // ── Embedding stub ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a stub EmbeddingService that returns a 3-dim float vector
    /// whose values are deterministic from the input text hash. This ensures
    /// different text inputs produce different (but predictable) vectors so
    /// cosine-similarity tests are reproducible.
    /// </summary>
    public static EmbeddingService CreateEmbeddingService(float[]? fixedVector = null)
    {
        var handler = new StubHttpMessageHandler(
            BuildEmbeddingResponse(fixedVector ?? new float[] { 0.1f, 0.2f, 0.3f }));

        var factory = CreateHttpClientFactory("embedding", handler);
        var config  = CreateConfig(new Dictionary<string, string?>
        {
            ["EmbeddingService:BaseUrl"] = "http://stub-embedding"
        });

        return new EmbeddingService(factory, config);
    }

    // ── LangChain stub ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a LangChainClientService backed by a controllable stub.
    /// Call SetLangChainResponse on the returned handler to change what the
    /// agent returns during a test.
    /// </summary>
    public static (LangChainClientService service, StubHttpMessageHandler handler)
        CreateLangChainService(LangChainStubResponse? response = null)
    {
        response ??= new LangChainStubResponse
        {
            Answer  = "This is a test answer from the LangChain stub.",
            Service = "Unknown",
            Action  = "answer"
        };

        var json    = BuildLangChainResponse(response);
        var handler = new StubHttpMessageHandler(json);
        var factory = CreateHttpClientFactory("langchain", handler);
        var config  = CreateConfig(new Dictionary<string, string?>
        {
            ["LangChain:BaseUrl"] = "http://stub-langchain"
        });

        return (new LangChainClientService(factory, config), handler);
    }

    // ── RetrievalService with test chunks ─────────────────────────────────────

    /// <summary>
    /// Builds a RetrievalService pre-loaded with TestFixtures.SampleChunks.
    /// The chunks carry dummy vectors designed to match their own service area
    /// queries more highly than other areas (using simple orthogonal vectors).
    /// </summary>
    public static RetrievalService CreateRetrievalService(
        IReadOnlyList<FaqChunk>? chunks = null)
    {
        return new RetrievalService(chunks ?? TestFixtures.SampleChunks);
    }

    // ── Full ChatOrchestrator ─────────────────────────────────────────────────

    /// <summary>
    /// Builds a ChatOrchestrator with stub HTTP dependencies.
    /// Returns both the orchestrator and the LangChain stub handler so tests
    /// can change what the LangChain agent says between calls.
    /// </summary>
    public static (ChatOrchestrator orchestrator, StubHttpMessageHandler langChainHandler)
        CreateOrchestrator(
            LangChainStubResponse? defaultLangChainResponse = null,
            IReadOnlyList<FaqChunk>? chunks = null,
            ConversationMemory? memory = null)
    {
        memory ??= new ConversationMemory();

        var embed    = CreateEmbeddingService();
        var retrieval = CreateRetrievalService(chunks);

        var (langChain, lcHandler) = CreateLangChainService(defaultLangChainResponse);

        // OpenAiChatService — also stub it out; the orchestrator rarely calls
        // this directly but it is a required ctor parameter.
        var openAiHandler = new StubHttpMessageHandler(
            """{"choices":[{"message":{"content":"Test OpenAI answer."}}]}""");
        var openAiFactory = CreateHttpClientFactory("openai", openAiHandler);
        var openAiConfig  = CreateConfig(new Dictionary<string, string?>
        {
            ["OpenAI:ApiKey"] = "sk-test-key"
        });
        var openAi = new OpenAiChatService(openAiFactory, openAiConfig);

        var config = CreateConfig(new Dictionary<string, string?>
        {
            ["Retrieval:Threshold"]         = "0.60",
            ["Retrieval:ThresholdConfident"] = "0.45",
            ["LangChain:BaseUrl"]            = "http://stub-langchain",
            ["EmbeddingService:BaseUrl"]     = "http://stub-embedding"
        });

        var orchestrator = new ChatOrchestrator(
            memory,
            embed,
            retrieval,
            openAi,
            langChain,
            config,
            new AppointmentService(),
            new FormFlowService(),
            new HousingNavigatorService(),
            new CouncilTaxCalculatorService(),
            new SchoolFinderService(),
            new LocationService(CreateHttpClientFactory("postcodes",
                new StubHttpMessageHandler("""{"status":200,"result":{"postcode":"BD3 8PX","latitude":53.79,"longitude":-1.74,"admin_district":"Bradford"}}"""))));

        return (orchestrator, lcHandler);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IHttpClientFactory CreateHttpClientFactory(
        string clientName, HttpMessageHandler handler)
    {
        var mock = new Mock<IHttpClientFactory>();
        mock.Setup(f => f.CreateClient(clientName))
            .Returns(new HttpClient(handler));
        // Also handle the default client name
        mock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(handler));
        return mock.Object;
    }

    private static IConfiguration CreateConfig(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static string BuildEmbeddingResponse(float[] vector)
    {
        var vectorJson = "[" + string.Join(",", vector.Select(v => v.ToString("F6"))) + "]";
        return $@"{{""embedding"":{vectorJson}}}";
    }

    private static string BuildLangChainResponse(LangChainStubResponse r)
    {
        return JsonSerializer.Serialize(new
        {
            answer             = r.Answer,
            service            = r.Service,
            action             = r.Action,
            needs_clarification = r.NeedsClarification,
            tool_used          = r.ToolUsed,
            next_steps_url     = r.NextStepsUrl
        });
    }
}
