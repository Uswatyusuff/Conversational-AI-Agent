using Microsoft.Playwright;
using CouncilChatbotPrototype.Models;
using System.Globalization;

namespace CouncilChatbotPrototype.Services;

public class PlaywrightService
{
    private readonly IConfiguration _config;
    private readonly string _targetUrl;
    private readonly bool _headless;
    private readonly int _waitAfterSubmitMs;
    private readonly int _timeoutMs;

    public PlaywrightService(IConfiguration config)
    {
        _config = config;
        _targetUrl = _config["Playwright:TargetUrl"]
            ?? "https://onlineforms.bradford.gov.uk/ufs/collectiondates.eb";

        _headless = bool.TryParse(_config["Playwright:Headless"], out var parsedHeadless)
            ? parsedHeadless
            : true;

        _waitAfterSubmitMs = int.TryParse(_config["Playwright:WaitAfterSubmitMs"], out var parsedWait)
            ? parsedWait
            : 3000;

        _timeoutMs = int.TryParse(_config["Playwright:TimeoutMs"], out var parsedTimeout)
            ? parsedTimeout
            : 60000;
    }

    public async Task<AddressLookupResult> GetAddressesByPostcodeAsync(string postcode)
    {
        var result = new AddressLookupResult
        {
            Postcode = postcode?.Trim() ?? ""
        };

        if (string.IsNullOrWhiteSpace(result.Postcode))
        {
            result.Error = "Postcode is required.";
            return result;
        }

        try
        {
            using var playwright = await Playwright.CreateAsync();

            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = _headless
            });

            var page = await browser.NewPageAsync();
            page.SetDefaultTimeout(_timeoutMs);

            await page.GotoAsync(_targetUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = _timeoutMs
            });

            await FillPostcodeAndSubmitAsync(page, result.Postcode);

            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await page.WaitForTimeoutAsync(_waitAfterSubmitMs);

            var addresses = await ExtractAddressButtonsAsync(page);

            if (addresses.Count == 0)
            {
                var bodyText = await SafeGetBodyTextAsync(page);

                Console.WriteLine("===== PAGE BODY AFTER POSTCODE SEARCH =====");
                Console.WriteLine(bodyText);
                Console.WriteLine("===========================================");

                result.Error = "No address buttons were returned for that postcode.";
                return result;
            }

            result.Addresses = addresses;
            return result;
        }
        catch (TimeoutException)
        {
            result.Error = "The postcode lookup timed out.";
            return result;
        }
        catch (Exception ex)
        {
            result.Error = $"Playwright error: {ex.Message}";
            return result;
        }
    }

    public async Task<string> GetBinResultForAddressAsync(string postcode, string selectedAddress)
    {
        if (string.IsNullOrWhiteSpace(postcode))
            return "Postcode is required.";

        if (string.IsNullOrWhiteSpace(selectedAddress))
            return "Address is required.";

        try
        {
            using var playwright = await Playwright.CreateAsync();

            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = _headless
            });

            var page = await browser.NewPageAsync();
            page.SetDefaultTimeout(_timeoutMs);

            await page.GotoAsync(_targetUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = _timeoutMs
            });

            await FillPostcodeAndSubmitAsync(page, postcode.Trim());

            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await page.WaitForTimeoutAsync(_waitAfterSubmitMs);

            var clickedAddress = await ClickAddressButtonAsync(page, selectedAddress.Trim());

            if (!clickedAddress)
                return $"Could not find a matching address button for: {selectedAddress}";

            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await page.WaitForTimeoutAsync(_waitAfterSubmitMs);

            Console.WriteLine("===== PAGE BODY AFTER ADDRESS CLICK =====");
            Console.WriteLine(await SafeGetBodyTextAsync(page));
            Console.WriteLine("=========================================");

            var clickedShowDates = await ClickShowCollectionDatesAsync(page);

            if (!clickedShowDates)
                return "The address page opened, but the 'Show collection dates' button could not be found.";

            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await page.WaitForTimeoutAsync(_waitAfterSubmitMs);

            var resultText = await ExtractCollectionResultsAsync(page);

            Console.WriteLine("===== PAGE BODY AFTER SHOW COLLECTION DATES =====");
            Console.WriteLine(resultText);
            Console.WriteLine("=================================================");

            return string.IsNullOrWhiteSpace(resultText)
                ? "No bin collection information found."
                : resultText.Trim();
        }
        catch (TimeoutException)
        {
            return "The bin result lookup timed out.";
        }
        catch (Exception ex)
        {
            return $"Playwright error: {ex.Message}";
        }
    }

    private async Task FillPostcodeAndSubmitAsync(IPage page, string postcode)
    {
        var postcodeInput = page.Locator("input[type='text']").First;

        await postcodeInput.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = _timeoutMs
        });

        await postcodeInput.FillAsync(postcode);
        await postcodeInput.PressAsync("Tab");

        var exactFindButton = page.GetByRole(AriaRole.Button, new() { Name = "Find address" });

        if (await exactFindButton.CountAsync() > 0)
        {
            await exactFindButton.First.ClickAsync();
            return;
        }

        var genericButtons = page.Locator("button, input[type='submit'], input[type='button']");
        var count = await genericButtons.CountAsync();

        for (int i = 0; i < count; i++)
        {
            var text = await SafeGetElementTextAsync(genericButtons.Nth(i));

            if (text.Contains("find", StringComparison.OrdinalIgnoreCase) &&
                text.Contains("address", StringComparison.OrdinalIgnoreCase))
            {
                await genericButtons.Nth(i).ClickAsync();
                return;
            }
        }

        throw new Exception("Could not find the 'Find address' button.");
    }

    private async Task<List<string>> ExtractAddressButtonsAsync(IPage page)
    {
        var addresses = new List<string>();

        var buttons = page.GetByRole(AriaRole.Button);
        var count = await buttons.CountAsync();

        for (int i = 0; i < count; i++)
        {
            var text = (await SafeGetElementTextAsync(buttons.Nth(i))).Trim();

            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (IsIgnoredButton(text))
                continue;

            if (!LooksLikeAddressOption(text))
                continue;

            addresses.Add(text);
        }

        if (addresses.Count > 0)
        {
            return addresses
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();
        }

        var fallbackElements = page.Locator("button, a, input[type='button'], input[type='submit']");
        var fallbackCount = await fallbackElements.CountAsync();

        for (int i = 0; i < fallbackCount; i++)
        {
            var text = (await SafeGetElementTextAsync(fallbackElements.Nth(i))).Trim();

            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (IsIgnoredButton(text))
                continue;

            if (!LooksLikeAddressOption(text))
                continue;

            addresses.Add(text);
        }

        return addresses
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
    }

    private async Task<bool> ClickAddressButtonAsync(IPage page, string selectedAddress)
    {
        var exactButton = page.GetByRole(AriaRole.Button, new()
        {
            Name = selectedAddress,
            Exact = true
        });

        if (await exactButton.CountAsync() > 0)
        {
            await exactButton.First.ClickAsync();
            return true;
        }

        var buttons = page.GetByRole(AriaRole.Button);
        var count = await buttons.CountAsync();

        for (int i = 0; i < count; i++)
        {
            var text = (await SafeGetElementTextAsync(buttons.Nth(i))).Trim();

            if (string.Equals(
                NormalizeForComparison(text),
                NormalizeForComparison(selectedAddress),
                StringComparison.OrdinalIgnoreCase))
            {
                await buttons.Nth(i).ClickAsync();
                return true;
            }
        }

        var fallbackElements = page.Locator("button, a, input[type='button'], input[type='submit']");
        var fallbackCount = await fallbackElements.CountAsync();

        for (int i = 0; i < fallbackCount; i++)
        {
            var text = (await SafeGetElementTextAsync(fallbackElements.Nth(i))).Trim();

            if (string.Equals(
                NormalizeForComparison(text),
                NormalizeForComparison(selectedAddress),
                StringComparison.OrdinalIgnoreCase))
            {
                await fallbackElements.Nth(i).ClickAsync();
                return true;
            }
        }

        return false;
    }

    private async Task<bool> ClickShowCollectionDatesAsync(IPage page)
    {
        var exactButton = page.GetByRole(AriaRole.Button, new()
        {
            Name = "Show collection dates",
            Exact = true
        });

        if (await exactButton.CountAsync() > 0)
        {
            await exactButton.First.ClickAsync();
            return true;
        }

        var allButtons = page.Locator("button, a, input[type='button'], input[type='submit']");
        var count = await allButtons.CountAsync();

        for (int i = 0; i < count; i++)
        {
            var text = (await SafeGetElementTextAsync(allButtons.Nth(i))).Trim();

            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (text.Contains("show", StringComparison.OrdinalIgnoreCase) &&
                text.Contains("collection", StringComparison.OrdinalIgnoreCase) &&
                text.Contains("date", StringComparison.OrdinalIgnoreCase))
            {
                await allButtons.Nth(i).ClickAsync();
                return true;
            }
        }

        return false;
    }

    private async Task<string> ExtractCollectionResultsAsync(IPage page)
{
    var bodyText = await SafeGetBodyTextAsync(page);

    if (string.IsNullOrWhiteSpace(bodyText))
        return "";

    var lines = bodyText
        .Replace("\r", "")
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(x => x.Trim())
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Where(x => !IsIgnoredResultLine(x))
        .ToList();

    var nextCollectionText = ExtractNextCollectionFromSummary(lines);

    var generalDates = ExtractDatesUnderExactHeading(
        lines,
        "General waste",
        new[] { "Recycling waste", "Garden waste", "Garden waste (subscription only)", "Print/save collection dates" });

    var recyclingDates = ExtractDatesUnderExactHeading(
        lines,
        "Recycling waste",
        new[] { "Garden waste", "Garden waste (subscription only)", "Print/save collection dates" });

    var gardenDates = ExtractDatesUnderExactHeading(
        lines,
        "Garden waste (subscription only)",
        new[] { "Print/save collection dates" });

    var generalEligible = generalDates.Count > 0;

    if (!generalEligible)
        return "This location is not eligible for bin collection.";

    // Recycling is automatically eligible if general waste is eligible.
    var recyclingEligible = true;

    // Garden subscription must be detected from the sentence under "Garden waste"
    var gardenStatusText = ExtractGardenSubscriptionText(lines);

    var gardenSubscribed =
        gardenStatusText.Contains("currently subscribed", StringComparison.OrdinalIgnoreCase) &&
        !gardenStatusText.Contains("not currently subscribed", StringComparison.OrdinalIgnoreCase);

    var gardenNotSubscribed =
        gardenStatusText.Contains("not currently subscribed", StringComparison.OrdinalIgnoreCase);

    // Only show garden dates when the page says subscribed and dates exist
    var gardenEligible = gardenSubscribed && gardenDates.Count > 0;

    var summary = new List<string>();

    if (!string.IsNullOrWhiteSpace(nextCollectionText))
    {
        summary.Add(nextCollectionText);
        summary.Add("");
    }
    else
    {
        var allUpcoming = new List<(DateTime date, string type)>();
        AddParsedDates(allUpcoming, generalDates, "General waste");
        AddParsedDates(allUpcoming, recyclingDates, "Recycling waste");

        if (gardenEligible)
            AddParsedDates(allUpcoming, gardenDates, "Garden waste");

        var nextCollection = allUpcoming
            .OrderBy(x => x.date)
            .FirstOrDefault();

        if (nextCollection != default)
        {
            summary.Add($"Next collection: {nextCollection.type} on {FormatDate(nextCollection.date)}");
            summary.Add("");
        }
    }

    AppendTopThree(summary, "General waste", generalDates, false);
    summary.Add("");

    if (recyclingDates.Count > 0)
    {
        AppendTopThree(summary, "Recycling waste", recyclingDates, false);
    }
    else if (recyclingEligible)
    {
        summary.Add("Recycling waste:");
        summary.Add("- Eligible, but no collection dates could be extracted");
    }
    else
    {
        summary.Add("Recycling waste: Not eligible");
    }

    summary.Add("");

    summary.Add($"Garden waste subscription: {GetGardenSubscriptionLabel(gardenSubscribed, gardenNotSubscribed)}");
    summary.Add("");

    if (gardenEligible)
    {
        AppendTopThree(summary, "Garden waste", gardenDates, false);
    }
    else
    {
        summary.Add("Garden waste: Subscription required");
    }

    return string.Join(Environment.NewLine, summary).Trim();
}
private static string ExtractGardenSubscriptionText(List<string> lines)
{
    for (int i = 0; i < lines.Count; i++)
    {
        if (lines[i].Equals("Garden waste", StringComparison.OrdinalIgnoreCase))
        {
            var collected = new List<string>();

            for (int j = i + 1; j < lines.Count; j++)
            {
                var line = lines[j];

                if (line.Equals("General waste", StringComparison.OrdinalIgnoreCase) ||
                    line.Equals("Recycling waste", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Garden waste (subscription only)", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                collected.Add(line);
            }

            return string.Join(" ", collected);
        }
    }

    return "";
}

private static string GetGardenSubscriptionLabel(bool subscribed, bool notSubscribed)
{
    if (subscribed)
        return "Subscribed";

    if (notSubscribed)
        return "Not subscribed";

    return "Unknown";
}
    private static string ExtractNextCollectionFromSummary(List<string> lines)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Contains("Your next general/recycling collections are", StringComparison.OrdinalIgnoreCase))
            {
                for (int j = i + 1; j < Math.Min(i + 6, lines.Count); j++)
                {
                    var line = lines[j];

                    if (line.Contains("Recycling waste", StringComparison.OrdinalIgnoreCase))
                    {
                        var date = ExtractDateFromMixedLine(line);
                        if (date.HasValue)
                            return $"Next collection: Recycling waste on {FormatDate(date.Value)}";
                    }

                    if (line.Contains("General waste", StringComparison.OrdinalIgnoreCase))
                    {
                        var date = ExtractDateFromMixedLine(line);
                        if (date.HasValue)
                            return $"Next collection: General waste on {FormatDate(date.Value)}";
                    }
                }
            }
        }

        return "";
    }

    private static DateTime? ExtractDateFromMixedLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i <= words.Length - 4; i++)
        {
            var candidate = string.Join(" ", words.Skip(i).Take(4));
            var parsed = TryParseCollectionDate(candidate);

            if (parsed.HasValue)
                return parsed;
        }

        return null;
    }

    private static List<string> ExtractDatesUnderExactHeading(
        List<string> lines,
        string heading,
        string[] endHeadings)
    {
        var startIndex = -1;

        for (int i = 0; i < lines.Count; i++)
        {
            if (string.Equals(lines[i], heading, StringComparison.OrdinalIgnoreCase))
            {
                startIndex = i;
                break;
            }
        }

        if (startIndex < 0)
            return new List<string>();

        var endIndex = lines.Count;

        for (int i = startIndex + 1; i < lines.Count; i++)
        {
            if (endHeadings.Any(h => string.Equals(lines[i], h, StringComparison.OrdinalIgnoreCase)))
            {
                endIndex = i;
                break;
            }
        }

        return lines
            .Skip(startIndex + 1)
            .Take(endIndex - startIndex - 1)
            .Where(LooksLikeCollectionDate)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AppendTopThree(List<string> summary, string label, List<string> dates, bool notEligible)
    {
        if (notEligible || dates.Count == 0)
        {
            summary.Add($"{label}: Not eligible");
            return;
        }

        summary.Add($"{label}:");

        foreach (var date in GetTopThreeDates(dates))
        {
            summary.Add($"- {date}");
        }
    }

    private static List<string> GetTopThreeDates(List<string> rawDates)
    {
        return rawDates
            .Select(TryParseCollectionDate)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .OrderBy(x => x)
            .Take(3)
            .Select(FormatDate)
            .ToList();
    }

    private static void AddParsedDates(List<(DateTime date, string type)> all, List<string> rawDates, string type)
    {
        foreach (var raw in rawDates)
        {
            var parsed = TryParseCollectionDate(raw);
            if (parsed.HasValue)
            {
                all.Add((parsed.Value, type));
            }
        }
    }

    private static DateTime? TryParseCollectionDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var formats = new[]
        {
            "ddd MMM dd yyyy",
            "ddd MMM d yyyy"
        };

        if (DateTime.TryParseExact(
            value.Trim(),
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string FormatDate(DateTime date)
    {
        return date.ToString("ddd dd MMM yyyy", CultureInfo.InvariantCulture);
    }

    private static bool LooksLikeCollectionDate(string line)
    {
        return TryParseCollectionDate(line).HasValue;
    }

    private static bool IsIgnoredButton(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var ignored = new[]
        {
            "Find address",
            "Find out more",
            "Privacy notice",
            "How we use your information",
            "Contact us online",
            "Cookies",
            "Accessibility",
            "A to Z",
            "Close",
            "Show collection dates",
            "Search again",
            "Search for another address",
            "View our Privacy notice",
            "Save"
        };

        return ignored.Any(x => string.Equals(text.Trim(), x, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeAddressOption(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return text.Contains("BD", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("ROAD", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("STREET", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("LANE", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("AVENUE", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("HOUSE", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("FLOOR", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("BRADFORD", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIgnoredResultLine(string text)
    {
        var ignoredFragments = new[]
        {
            "Privacy notice",
            "How we use your information",
            "Contact us online",
            "Find out more",
            "Cookies",
            "Accessibility",
            "A to Z",
            "Search again",
            "Search for another address",
            "Show collection dates",
            "View our Privacy notice",
            "Bradford Council sends regular bulletins",
            "You can opt in to receive relevant information",
            "For security purposes this form will time out after 20 minutes of inactivity",
            "Save address",
            "Print/save collection dates",
            "Print/save with images",
            "Print/save without images",
            "For more all information regarding garden waste collections visit our garden waste collections webpage",
            "Please note that this save feature will save a cookie",
            "If you wish to save this address select the Save button below"
        };

        return ignoredFragments.Any(x => text.Contains(x, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeForComparison(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return value
            .Replace(" ,", ",")
            .Replace(", ", ",")
            .Replace("  ", " ")
            .Trim()
            .ToUpperInvariant();
    }

    private static async Task<string> SafeGetBodyTextAsync(IPage page)
    {
        try
        {
            return await page.Locator("body").InnerTextAsync();
        }
        catch
        {
            return "";
        }
    }

    private static async Task<string> SafeGetElementTextAsync(ILocator locator)
    {
        try
        {
            var text = await locator.InnerTextAsync();
            if (!string.IsNullOrWhiteSpace(text))
                return text.Trim();
        }
        catch
        {
        }

        try
        {
            var value = await locator.GetAttributeAsync("value");
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        catch
        {
        }

        try
        {
            var ariaLabel = await locator.GetAttributeAsync("aria-label");
            if (!string.IsNullOrWhiteSpace(ariaLabel))
                return ariaLabel.Trim();
        }
        catch
        {
        }

        return "";
    }
}