using System.Text.Json;
using CouncilChatbotPrototype.Models;
using CouncilChatbotPrototype.Services;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using DotNetEnv;

var builder = WebApplication.CreateBuilder(args);

Env.Load(); // loads variables from .env into environment

builder.Services.AddControllers();

// Named client for OpenAI (fallback only)
builder.Services.AddHttpClient("openai", client =>
{
    client.BaseAddress = new Uri("https://api.openai.com/v1/");
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.Accept.Add(
        new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
});

// Named client for embedding service
builder.Services.AddHttpClient("embedding", client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});

// Named client for LangChain service
builder.Services.AddHttpClient("langchain", client =>
{
    client.Timeout = TimeSpan.FromSeconds(90);
});

// Named client for postcodes.io — real postcode-to-coordinates lookup
builder.Services.AddHttpClient("postcodes", client =>
{
    client.BaseAddress = new Uri("https://api.postcodes.io/");
    client.Timeout     = TimeSpan.FromSeconds(5);
});

builder.Services.AddSingleton<PlaywrightService>();

builder.Services.AddSingleton<EmbeddingService>();
builder.Services.AddSingleton<ConversationMemory>();
builder.Services.AddSingleton<LoggingService>();
builder.Services.AddSingleton<OpenAiChatService>();
builder.Services.AddSingleton<LangChainClientService>();

// ── New services (Phase 2 additions) ────────────────────────────────────────
builder.Services.AddSingleton<LocationService>();
builder.Services.AddSingleton<AppointmentService>();
builder.Services.AddSingleton<FormFlowService>();
builder.Services.AddSingleton<HousingNavigatorService>();
builder.Services.AddSingleton<CouncilTaxCalculatorService>();
builder.Services.AddSingleton<SchoolFinderService>();
// ── Rate limiting ────────────────────────────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("chat", limiterOptions =>
    {
        limiterOptions.PermitLimit       = 20;
        limiterOptions.Window            = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit        = 5;
    });
    options.RejectionStatusCode = 429;
});

builder.Services.AddSingleton<ChatOrchestrator>();

var dataDir = Path.Combine(builder.Environment.ContentRootPath, "Data");
var logsDir = Path.Combine(builder.Environment.ContentRootPath, "Logs");

Directory.CreateDirectory(dataDir);
Directory.CreateDirectory(logsDir);

var faqPath = Path.Combine(dataDir, "faqs.json");
var chunksCachePath = Path.Combine(dataDir, "chunks.embeddings.json");

var faqs = LoadFaqs(faqPath);
var chunks = ChunkingService.BuildChunks(faqs);

builder.Services.AddSingleton(faqs);
builder.Services.AddSingleton(chunks);

builder.Services.AddSingleton<List<FaqChunk>>(sp =>
{
    var embedSvc = sp.GetRequiredService<EmbeddingService>();

    return LoadOrCreateChunkEmbeddings(
        chunksCachePath,
        faqs,
        chunks,
        embedSvc
    ).GetAwaiter().GetResult();
});

builder.Services.AddSingleton<IReadOnlyList<FaqChunk>>(sp =>
    sp.GetRequiredService<List<FaqChunk>>());

builder.Services.AddSingleton<RetrievalService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.MapControllers();

app.Run();

static List<FaqItem> LoadFaqs(string path)
{
    if (!File.Exists(path))
        return new();

    return JsonSerializer.Deserialize<List<FaqItem>>(
        File.ReadAllText(path),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
    ) ?? new();
}

static string FingerprintFaqs(List<FaqItem> faqs)
{
    var raw = string.Join("||", faqs.Select(f =>
        $"{f.Service}::{f.Title}::{f.Answer}::{f.NextStepsUrl}"
    ));

    using var sha = System.Security.Cryptography.SHA256.Create();
    var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw));
    return Convert.ToHexString(bytes);
}

static async Task<List<FaqChunk>> LoadOrCreateChunkEmbeddings(
    string cachePath,
    List<FaqItem> faqs,
    List<FaqChunk> chunks,
    EmbeddingService embedSvc)
{
    var fingerprint = FingerprintFaqs(faqs);

    if (File.Exists(cachePath))
    {
        var cached = JsonSerializer.Deserialize<CachedChunkContainer>(
            await File.ReadAllTextAsync(cachePath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        if (cached != null &&
            cached.Fingerprint == fingerprint &&
            cached.Items.Count == chunks.Count &&
            cached.Items.All(c => c.Vector != null && c.Vector.Length > 0))
        {
            Console.WriteLine($"✅ Using cached embeddings: {cached.Items.Count}");
            return cached.Items;
        }
    }

    Console.WriteLine("⏳ Building chunk embeddings...");

    for (int i = 0; i < chunks.Count; i++)
    {
        var text = $"{chunks[i].Service}\n{chunks[i].Title}\n{chunks[i].Text}";
        chunks[i].Vector = await embedSvc.EmbedAsync(text);
        Console.WriteLine($"   Embedded chunk {i + 1}/{chunks.Count}");
    }

    var container = new CachedChunkContainer
    {
        Fingerprint = fingerprint,
        Items = chunks
    };

    await File.WriteAllTextAsync(
        cachePath,
        JsonSerializer.Serialize(
            container,
            new JsonSerializerOptions { WriteIndented = true }
        )
    );

    Console.WriteLine($"✅ Embedded chunks ready: {chunks.Count}");

    return chunks;
}

class CachedChunkContainer
{
    public string Fingerprint { get; set; } = "";
    public List<FaqChunk> Items { get; set; } = new();
}