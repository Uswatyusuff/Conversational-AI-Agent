using System.Text.Json;
using System.Text.RegularExpressions;

namespace CouncilChatbotPrototype.Services;

public class LoggingService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public async Task LogChatAsync(object obj)
        => await AppendJsonLine("Logs/chatlog.jsonl", obj);

    public async Task LogFeedbackAsync(object obj)
        => await AppendJsonLine("Logs/feedback.jsonl", obj);

    private static async Task AppendJsonLine(string path, object obj)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var sanitized = SanitizeObject(obj);
        var line = JsonSerializer.Serialize(sanitized, JsonOptions);

        await File.AppendAllTextAsync(path, line + Environment.NewLine);
    }

    private static object? SanitizeObject(object? value)
    {
        if (value is null)
            return null;

        if (value is string s)
            return SanitizeString(s);

        if (value is IDictionary<string, object?> dictObj)
        {
            var output = new Dictionary<string, object?>();
            foreach (var kv in dictObj)
                output[kv.Key] = SanitizeObject(kv.Value);

            return output;
        }

        if (value is IDictionary<string, string> dictString)
        {
            var output = new Dictionary<string, object?>();
            foreach (var kv in dictString)
                output[kv.Key] = SanitizeString(kv.Value);

            return output;
        }

        if (value is IEnumerable<object?> list && value is not string)
        {
            return list.Select(SanitizeObject).ToList();
        }

        var json = JsonSerializer.Serialize(value);
        using var doc = JsonDocument.Parse(json);
        return SanitizeJsonElement(doc.RootElement);
    }

    private static object? SanitizeJsonElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var output = new Dictionary<string, object?>();
                foreach (var prop in element.EnumerateObject())
                    output[prop.Name] = SanitizeJsonElement(prop.Value);
                return output;
            }

            case JsonValueKind.Array:
            {
                var output = new List<object?>();
                foreach (var item in element.EnumerateArray())
                    output.Add(SanitizeJsonElement(item));
                return output;
            }

            case JsonValueKind.String:
                return SanitizeString(element.GetString() ?? "");

            case JsonValueKind.Number:
                if (element.TryGetInt64(out var l)) return l;
                if (element.TryGetDouble(out var d)) return d;
                return element.ToString();

            case JsonValueKind.True:
            case JsonValueKind.False:
                return element.GetBoolean();

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;

            default:
                return element.ToString();
        }
    }

    private static string SanitizeString(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input ?? "";

        var output = input;

        // UK postcode
        output = Regex.Replace(
            output,
            @"\b[A-Z]{1,2}\d[A-Z\d]?\s?\d[A-Z]{2}\b",
            "[POSTCODE]",
            RegexOptions.IgnoreCase);

        // Email
        output = Regex.Replace(
            output,
            @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b",
            "[EMAIL]",
            RegexOptions.IgnoreCase);

        // UK phone number
        output = Regex.Replace(
            output,
            @"\b(?:\+44|0)\d[\d\s]{8,}\b",
            "[PHONE]",
            RegexOptions.IgnoreCase);

        // Very broad council/address-style line masking
        output = Regex.Replace(
            output,
            @"\b\d+\s*,?\s*[A-Z0-9\s,'-]{6,}\b",
            "[ADDRESS]",
            RegexOptions.IgnoreCase);

        return output;
    }
}