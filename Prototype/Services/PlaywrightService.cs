using Microsoft.Playwright;
using CouncilChatbotPrototype.Models;

namespace CouncilChatbotPrototype.Services;

public class PlaywrightService
{
    private readonly IConfiguration _config;
    private readonly string _binUrl;
    private readonly bool _headless;
    private readonly int _waitAfterSubmitMs;

    public PlaywrightService(IConfiguration config)
    {
        _config = config;
        _binUrl = _config["Playwright:TargetUrl"]
            ?? "https://onlineforms.bradford.gov.uk/ufs/collectiondates.eb";

        _headless = bool.TryParse(_config["Playwright:Headless"], out var parsedHeadless)
            ? parsedHeadless
            : true;

        _waitAfterSubmitMs = int.TryParse(_config["Playwright:WaitAfterSubmitMs"], out var parsedWait)
            ? parsedWait
            : 2000;
    }

    public async Task<AddressLookupResult> GetAddressesByPostcodeAsync(string postcode)
    {
        var result = new AddressLookupResult { Postcode = postcode };

        try
        {
            using var playwright = await Playwright.CreateAsync();

            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = _headless
            });

            var page = await browser.NewPageAsync();

            await page.GotoAsync(_binUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            });

            await page.Locator("input[type='text']").First.FillAsync(postcode);
            await page.GetByRole(AriaRole.Button, new() { Name = "Find address" }).ClickAsync();

            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await page.WaitForTimeoutAsync(_waitAfterSubmitMs);

            var bodyText = await page.Locator("body").InnerTextAsync();

            if (!bodyText.Contains("Please select the address", StringComparison.OrdinalIgnoreCase))
            {
                result.Error = "No address list was returned for that postcode.";
                return result;
            }

            var buttons = page.GetByRole(AriaRole.Button);
            var count = await buttons.CountAsync();

            var addresses = new List<string>();

            for (int i = 0; i < count; i++)
            {
                var text = (await buttons.Nth(i).InnerTextAsync()).Trim();

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (text.Contains(postcode, StringComparison.OrdinalIgnoreCase))
                    addresses.Add(text);
            }

            result.Addresses = addresses
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (result.Addresses.Count == 0)
                result.Error = "Address section was found, but no clickable address buttons were extracted.";

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
        try
        {
            using var playwright = await Playwright.CreateAsync();

            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = _headless
            });

            var page = await browser.NewPageAsync();

            await page.GotoAsync(_binUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            });

            await page.Locator("input[type='text']").First.FillAsync(postcode);
            await page.GetByRole(AriaRole.Button, new() { Name = "Find address" }).ClickAsync();

            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await page.WaitForTimeoutAsync(_waitAfterSubmitMs);

            await page.GetByRole(AriaRole.Button, new() { Name = selectedAddress, Exact = true }).ClickAsync();

            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await page.WaitForTimeoutAsync(_waitAfterSubmitMs);

            var result = await page.Locator("body").InnerTextAsync();

            return string.IsNullOrWhiteSpace(result)
                ? "No bin collection information found."
                : result;
        }
        catch (Exception ex)
        {
            return $"Playwright error: {ex.Message}";
        }
    }
}