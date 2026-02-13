using System.Text.Json;
using CouncilChatbotPrototype.Models;
using CouncilChatbotPrototype.Services;

var builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers();

// Http clients
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("embeddings", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

// Core services
builder.Services.AddSingleton<EmbeddingService>();
builder.Services.AddSingleton<ConversationMemory>();
builder.Services.AddSingleton<ChatScoringService>();
builder.Services.AddSingleton<IntentService>();
builder.Services.AddSingleton<ResponseService>();
builder.Services.AddSingleton<LoggingService>();

// Paths
var dataDir = Path.Combine(builder.Environment.ContentRootPath, "Data");
var logsDir = Path.Combine(builder.Environment.ContentRootPath, "Logs");
Directory.CreateDirectory(dataDir);
Directory.CreateDirectory(logsDir);

var faqPath = Path.Combine(dataDir, "faqs.json");
var cachePath = Path.Combine(dataDir, "faqs.embeddings.json");

// ✅ Load FAQs ONCE at startup
var faqs = LoadFaqs(faqPath);

// ✅ Register repository using the loaded list
builder.Services.AddSingleton(new FaqRepository(faqs));

// ✅ Build embeddings cache once (when first resolved)
builder.Services.AddSingleton(provider =>
{
    var embedSvc = provider.GetRequiredService<EmbeddingService>();
    return LoadOrCreateEmbeddings(cachePath, faqs, embedSvc).GetAwaiter().GetResult();
});

// Retrieval service (uses cached embeddings)
builder.Services.AddSingleton(provider =>
{
    var embeddedFaqs = provider.GetRequiredService<List<(FaqItem faq, float[] embedding)>>();
    return new RetrievalService(embeddedFaqs);
});

// Orchestrator
builder.Services.AddSingleton<ChatOrchestrator>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

app.Run();


// ---------------- Helpers (Startup) ----------------

static List<FaqItem> LoadFaqs(string path)
{
    if (!File.Exists(path)) return new List<FaqItem>();

    var json = File.ReadAllText(path);

    return JsonSerializer.Deserialize<List<FaqItem>>(json,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
    ) ?? new List<FaqItem>();
}

// Fingerprint prevents “cache exists but FAQ content changed”
static string FingerprintFaqs(List<FaqItem> faqs)
{
    var joined = string.Join("||", faqs.Select(f => $"{f.Service}::{f.Title}::{f.Answer}"));
    return joined.GetHashCode().ToString();
}

static async Task<List<(FaqItem faq, float[] embedding)>> LoadOrCreateEmbeddings(
    string cachePath,
    List<FaqItem> faqs,
    EmbeddingService embedSvc)
{
    var fingerprint = FingerprintFaqs(faqs);

    // Reuse cache if valid and not empty/corrupt
    if (File.Exists(cachePath))
    {
        var info = new FileInfo(cachePath);
        if (info.Length > 0)
        {
            try
            {
                var cachedJson = await File.ReadAllTextAsync(cachePath);

                var cached = JsonSerializer.Deserialize<CachedContainer>(cachedJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (cached != null &&
                    cached.Fingerprint == fingerprint &&
                    cached.Items != null &&
                    cached.Items.Count == faqs.Count)
                {
                    return cached.Items.Select(c => (c.Faq, c.Embedding)).ToList();
                }
            }
            catch (JsonException)
            {
                // cache corrupted -> rebuild below
            }
        }
    }

    // Build fresh cache
    var items = new List<CachedFaq>();

    foreach (var faq in faqs)
    {
        var text = $"{faq.Service}\n{faq.Title}\n{faq.Answer}";
        var emb = await embedSvc.EmbedAsync(text);
        items.Add(new CachedFaq { Faq = faq, Embedding = emb });
    }

    var container = new CachedContainer { Fingerprint = fingerprint, Items = items };

    var outJson = JsonSerializer.Serialize(container, new JsonSerializerOptions { WriteIndented = true });
    await File.WriteAllTextAsync(cachePath, outJson);

    return items.Select(x => (x.Faq, x.Embedding)).ToList();
}

class CachedContainer
{
    public string Fingerprint { get; set; } = "";
    public List<CachedFaq> Items { get; set; } = new();
}

class CachedFaq
{
    public FaqItem Faq { get; set; } = new();
    public float[] Embedding { get; set; } = Array.Empty<float>();
}