using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace CouncilChatbotPrototype.Controllers;

/// <summary>
/// Lightweight proxy endpoints for OpenAI voice services.
/// POST /api/voice/transcribe  — speech-to-text via Whisper
/// POST /api/voice/speak       — text-to-speech via OpenAI TTS
/// Both use the same OpenAI API key and named HttpClient as the rest of the app.
/// </summary>
[ApiController]
public class VoiceController : ControllerBase
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration     _config;
    private readonly ILogger<VoiceController> _log;

    // Map browser MIME types to file extensions Whisper recognises.
    private static readonly Dictionary<string, string> _audioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["audio/webm"]             = ".webm",
        ["audio/webm;codecs=opus"] = ".webm",
        ["audio/ogg"]              = ".ogg",
        ["audio/ogg;codecs=opus"]  = ".ogg",
        ["audio/mp4"]              = ".mp4",
        ["audio/mpeg"]             = ".mp3",
        ["audio/wav"]              = ".wav",
        ["audio/wave"]             = ".wav",
        ["application/octet-stream"] = ".webm",  // fallback
    };

    public VoiceController(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<VoiceController> log)
    {
        _httpFactory = httpFactory;
        _config      = config;
        _log         = log;

        // Log at startup so misconfiguration is immediately visible in the console.
        // The key is read from config key "OpenAI:ApiKey", which maps to:
        //   appsettings.json  →  "OpenAI": { "ApiKey": "sk-..." }
        //   environment var   →  OpenAI__ApiKey=sk-...   (double underscore)
        // NOTE: a single-underscore env var (OPENAI_API_KEY) will NOT be picked up here.
        var key = config["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(key))
            _log.LogWarning("[VoiceController] OpenAI:ApiKey is not set. Voice endpoints will return 503. " +
                            "Set it via appsettings or the env var OpenAI__ApiKey (double underscore).");
        else
            _log.LogInformation("[VoiceController] OpenAI API key is configured (length {Len}).", key.Length);
    }

    // ── POST /api/voice/transcribe ────────────────────────────────────────────
    // Accepts multipart/form-data with field "audio" (audio file).
    // Returns: { "transcript": "..." }
    [HttpPost("/api/voice/transcribe")]
    [RequestSizeLimit(25 * 1024 * 1024)]  // 25 MB — matches Whisper file limit
    public async Task<IActionResult> Transcribe([FromForm] IFormFile? audio, CancellationToken ct)
    {
        if (audio == null || audio.Length == 0)
            return BadRequest(new { error = "No audio file received." });

        var apiKey = _config["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return StatusCode(503, new { error = "Voice service not configured." });

        // Determine filename extension so Whisper can detect the codec.
        var rawContentType = audio.ContentType ?? "application/octet-stream";
        var baseMime = rawContentType.Split(';')[0].Trim();
        var ext = _audioExtensions.TryGetValue(rawContentType, out var e1) ? e1
                : _audioExtensions.TryGetValue(baseMime, out var e2) ? e2
                : ".webm";
        var fileName = "recording" + ext;

        try
        {
            using var multipart = new MultipartFormDataContent();

            // Copy audio into a memory buffer so we control lifetime.
            using var ms = new MemoryStream();
            await audio.CopyToAsync(ms, ct);
            ms.Position = 0;

            var fileContent = new ByteArrayContent(ms.ToArray());
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(baseMime);
            multipart.Add(fileContent, "file", fileName);
            multipart.Add(new StringContent("whisper-1"), "model");
            multipart.Add(new StringContent("en"), "language");

            var client = _httpFactory.CreateClient("openai");
            using var req = new HttpRequestMessage(HttpMethod.Post, "audio/transcriptions");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Content = multipart;

            using var res = await client.SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
            {
                _log.LogWarning("Whisper API error {status}: {body}", (int)res.StatusCode, json);
                return StatusCode((int)res.StatusCode, new { error = "Transcription failed." });
            }

            using var doc = JsonDocument.Parse(json);
            var transcript = doc.RootElement.TryGetProperty("text", out var t)
                ? t.GetString() ?? ""
                : "";

            return Ok(new { transcript });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Transcribe error");
            return StatusCode(500, new { error = "Transcription error." });
        }
    }

    // ── POST /api/voice/speak ─────────────────────────────────────────────────
    // Accepts: { "text": "...", "voice": "alloy" (optional) }
    // Returns: audio/mpeg stream
    [HttpPost("/api/voice/speak")]
    public async Task<IActionResult> Speak([FromBody] SpeakRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body?.Text))
            return BadRequest(new { error = "No text provided." });

        var apiKey = _config["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return StatusCode(503, new { error = "Voice service not configured." });

        // Sanitise text: strip HTML tags and limit length.
        var clean = StripHtml(body.Text);
        if (clean.Length > 1000) clean = clean[..1000].TrimEnd() + "…";
        if (string.IsNullOrWhiteSpace(clean))
            return BadRequest(new { error = "Text is empty after sanitisation." });

        var voice = body.Voice switch
        {
            "echo"    => "echo",
            "fable"   => "fable",
            "onyx"    => "onyx",
            "nova"    => "nova",
            "shimmer" => "shimmer",
            _         => "alloy"   // default
        };

        var payload = JsonSerializer.Serialize(new
        {
            model  = "tts-1",
            input  = clean,
            voice,
            response_format = "mp3"
        });

        try
        {
            var client = _httpFactory.CreateClient("openai");
            using var req = new HttpRequestMessage(HttpMethod.Post, "audio/speech");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync(ct);
                _log.LogWarning("TTS API error {status}: {body}", (int)res.StatusCode, err);
                return StatusCode((int)res.StatusCode, new { error = "TTS failed." });
            }

            var audioBytes = await res.Content.ReadAsByteArrayAsync(ct);
            return File(audioBytes, "audio/mpeg");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Speak error");
            return StatusCode(500, new { error = "TTS error." });
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static string StripHtml(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        // Remove HTML tags
        var noTags = System.Text.RegularExpressions.Regex.Replace(input, "<[^>]+>", " ");
        // Collapse whitespace
        return System.Text.RegularExpressions.Regex.Replace(noTags, @"\s+", " ").Trim();
    }
}

public class SpeakRequest
{
    public string Text  { get; set; } = "";
    public string Voice { get; set; } = "alloy";
}
