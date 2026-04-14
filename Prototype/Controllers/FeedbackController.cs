using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace CouncilChatbotPrototype.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FeedbackController : ControllerBase
{
    private readonly IWebHostEnvironment _env;

    public FeedbackController(IWebHostEnvironment env)
    {
        _env = env;
    }

    [HttpGet("summary")]
    public IActionResult GetSummary()
    {
        var logPath = Path.Combine(_env.ContentRootPath, "logs", "feedback.jsonl");

        if (!System.IO.File.Exists(logPath))
        {
            return Ok(new
            {
                total = 0,
                helpful = 0,
                notHelpful = 0,
                satisfactionRate = 0,
                comments = Array.Empty<object>()
            });
        }

        var lines = System.IO.File.ReadAllLines(logPath);
        var items = new List<Dictionary<string, object>>();

        foreach (var line in lines)
        {
            try
            {
                var obj = JsonSerializer.Deserialize<Dictionary<string, object>>(line);
                if (obj != null) items.Add(obj);
            }
            catch
            {
            }
        }

        int total = items.Count;
        int helpful = items.Count(x =>
            x.TryGetValue("Helpful", out var v) &&
            v?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true);

        int notHelpful = total - helpful;

        var comments = items
            .Where(x => x.TryGetValue("Comment", out var c) && !string.IsNullOrWhiteSpace(c?.ToString()))
            .Select(x => new
            {
                service = x.TryGetValue("Service", out var s) ? s?.ToString() : "",
                comment = x.TryGetValue("Comment", out var c) ? c?.ToString() : "",
                sessionId = x.TryGetValue("SessionId", out var sid) ? sid?.ToString() : ""
            })
            .ToList();

        var satisfactionRate = total > 0 ? Math.Round((double)helpful / total * 100, 2) : 0;

        // Per-service breakdown
        var serviceGroups = items
            .GroupBy(x => x.TryGetValue("service", out var sv)
                ? sv?.ToString() ?? "Unknown"
                : (x.TryGetValue("Service", out var sv2) ? sv2?.ToString() ?? "Unknown" : "Unknown"))
            .Select(g =>
            {
                int groupTotal = g.Count();
                int groupHelpful = g.Count(x =>
                    (x.TryGetValue("helpful", out var v) || x.TryGetValue("Helpful", out v)) &&
                    v?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true);
                return new
                {
                    service = g.Key,
                    total = groupTotal,
                    helpful = groupHelpful,
                    notHelpful = groupTotal - groupHelpful,
                    satisfactionRate = groupTotal > 0
                        ? Math.Round((double)groupHelpful / groupTotal * 100, 2)
                        : 0.0
                };
            })
            .OrderByDescending(g => g.total)
            .ToList();

        // Recent 50 comments (newest first)
        var recentComments = items
            .Where(x =>
            {
                var key = x.ContainsKey("comment") ? "comment" : x.ContainsKey("Comment") ? "Comment" : null;
                return key != null && !string.IsNullOrWhiteSpace(x[key]?.ToString());
            })
            .TakeLast(50)
            .Reverse()
            .Select(x => new
            {
                service = x.TryGetValue("service", out var s) ? s?.ToString()
                         : x.TryGetValue("Service", out var s2) ? s2?.ToString() : "",
                helpful = (x.TryGetValue("helpful", out var hv) || x.TryGetValue("Helpful", out hv)) &&
                           hv?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true,
                comment = x.TryGetValue("comment", out var c) ? c?.ToString()
                         : x.TryGetValue("Comment", out var c2) ? c2?.ToString() : "",
                ts      = x.TryGetValue("ts", out var t) ? t?.ToString() : ""
            })
            .ToList<object>();

        return Ok(new
        {
            total,
            helpful,
            notHelpful,
            satisfactionRate,
            byService = serviceGroups,
            comments = recentComments
        });
    }
}