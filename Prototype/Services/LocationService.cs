using System.Text.Json;
using CouncilChatbotPrototype.Models;

namespace CouncilChatbotPrototype.Services;

/// <summary>
/// Returns nearby Bradford Council services ordered by real Haversine distance.
///
/// Postcode-to-coordinates: https://api.postcodes.io/postcodes/{postcode}
/// On API failure the service falls back to Bradford city-centre coordinates
/// (53.7950, -1.7520) and sets NearbyServicesResponse.LocationNote so the caller
/// can surface an "approximate distances" warning if desired.
/// </summary>
public class LocationService
{
    private readonly IHttpClientFactory _httpClientFactory;

    // Fallback when postcodes.io is unreachable — Bradford City Hall
    private const double FallbackLat = 53.7950;
    private const double FallbackLng = -1.7520;

    public LocationService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    // ── Bradford Council Offices ─────────────────────────────────────────────────
    private static readonly List<CouncilLocation> CouncilOffices = new()
    {
        new()
        {
            Name         = "Bradford City Hall",
            Address      = "Centenary Square, Bradford, BD1 1HY",
            Phone        = "01274 431000",
            OpeningHours = "Mon–Fri 9:00 AM – 5:00 PM",
            Website      = "https://www.bradford.gov.uk/contact-us/contact-us-now/contact-us-now/",
            Notes        = "Main council headquarters. General enquiries and in-person services.",
            Lat = 53.7950, Lng = -1.7520
        },
        new()
        {
            Name         = "Keighley Area Office",
            Address      = "Town Hall, Bow Street, Keighley, BD21 3PA",
            Phone        = "01274 431000",
            OpeningHours = "Mon–Fri 9:00 AM – 4:30 PM",
            Website      = "https://www.bradford.gov.uk/contact-us/",
            Notes        = "Local area council services for the Keighley district.",
            Lat = 53.8680, Lng = -1.9060
        },
        new()
        {
            Name         = "Shipley Area Office",
            Address      = "Alexandra Road, Shipley, BD18 3EE",
            Phone        = "01274 431000",
            OpeningHours = "Mon–Fri 9:00 AM – 4:30 PM",
            Website      = "https://www.bradford.gov.uk/contact-us/",
            Notes        = "Local area council services for the Shipley district.",
            Lat = 53.8330, Lng = -1.7740
        }
    };

    // ── Bradford Libraries ────────────────────────────────────────────────────────
    private static readonly List<CouncilLocation> Libraries = new()
    {
        new()
        {
            Name         = "Bradford Central Library",
            Address      = "Princes Way, Bradford, BD1 1NN",
            Phone        = "01274 433600",
            OpeningHours = "Mon–Fri 9:00 AM – 7:00 PM, Sat 9:00 AM – 5:00 PM",
            Website      = "https://www.bradford.gov.uk/libraries/find-your-local-library/find-your-local-library/",
            Notes        = "Largest library in the district. Free Wi-Fi, computers, and digital services.",
            Lat = 53.7952, Lng = -1.7533
        },
        new()
        {
            Name         = "Manningham Library",
            Address      = "Carlisle Road, Bradford, BD8 8BB",
            Phone        = "01274 433600",
            OpeningHours = "Mon, Wed, Fri 10:00 AM – 5:00 PM, Sat 10:00 AM – 1:00 PM",
            Website      = "https://www.bradford.gov.uk/libraries/find-your-local-library/find-your-local-library/",
            Notes        = "Community library with children's and multicultural collections.",
            Lat = 53.8050, Lng = -1.7700
        },
        new()
        {
            Name         = "Keighley Library",
            Address      = "North Street, Keighley, BD21 3SX",
            Phone        = "01535 618215",
            OpeningHours = "Mon–Fri 9:00 AM – 5:30 PM, Sat 9:00 AM – 4:00 PM",
            Website      = "https://www.bradford.gov.uk/libraries/find-your-local-library/find-your-local-library/",
            Notes        = "Full-service library including local history archive.",
            Lat = 53.8680, Lng = -1.9055
        },
        new()
        {
            Name         = "Shipley Library",
            Address      = "2 Alexandra Road, Shipley, BD18 3EE",
            Phone        = "01274 433600",
            OpeningHours = "Mon–Fri 9:30 AM – 5:30 PM, Sat 9:30 AM – 1:00 PM",
            Website      = "https://www.bradford.gov.uk/libraries/find-your-local-library/find-your-local-library/",
            Notes        = "Local library serving Shipley and surrounding areas.",
            Lat = 53.8336, Lng = -1.7743
        },
        new()
        {
            Name         = "Ilkley Library",
            Address      = "Town Hall, Station Road, Ilkley, LS29 9EW",
            Phone        = "01274 433600",
            OpeningHours = "Mon–Fri 9:30 AM – 5:30 PM, Sat 9:30 AM – 1:00 PM",
            Website      = "https://www.bradford.gov.uk/libraries/find-your-local-library/find-your-local-library/",
            Notes        = "Library within Ilkley Town Hall.",
            Lat = 53.9252, Lng = -1.8218
        },
        new()
        {
            Name         = "Bingley Library",
            Address      = "Myrtle Place, Bingley, BD16 2LB",
            Phone        = "01274 433600",
            OpeningHours = "Mon–Fri 9:30 AM – 5:30 PM, Sat 9:30 AM – 1:00 PM",
            Website      = "https://www.bradford.gov.uk/libraries/find-your-local-library/find-your-local-library/",
            Notes        = "Community library with regular events and reading groups.",
            Lat = 53.8462, Lng = -1.8380
        }
    };

    // ── Household Waste Recycling Centres ────────────────────────────────────────
    private static readonly List<CouncilLocation> RecyclingCentres = new()
    {
        new()
        {
            Name         = "Bowling Back Lane HWRC",
            Address      = "Bowling Back Lane, Bradford, BD4 8SL",
            Phone        = "01274 431000",
            OpeningHours = "Mon–Sun 8:00 AM – 7:00 PM (Apr–Sep), 8:00 AM – 5:00 PM (Oct–Mar)",
            Website      = "https://www.bradford.gov.uk/recycling-and-waste/household-waste-recycling-centres/search-household-waste-sites/",
            Notes        = "Accepts: general waste, recycling, garden waste, electrical items, furniture.",
            Lat = 53.7820, Lng = -1.7350
        },
        new()
        {
            Name         = "Tyersal Lane HWRC",
            Address      = "Tyersal Lane, Bradford, BD4 8JU",
            Phone        = "01274 431000",
            OpeningHours = "Mon–Sun 8:00 AM – 7:00 PM (Apr–Sep), 8:00 AM – 5:00 PM (Oct–Mar)",
            Website      = "https://www.bradford.gov.uk/recycling-and-waste/household-waste-recycling-centres/search-household-waste-sites/",
            Notes        = "Accepts: general waste, metals, tyres (charges apply), garden waste.",
            Lat = 53.7791, Lng = -1.7272
        },
        new()
        {
            Name         = "Eccleshill HWRC",
            Address      = "Harrogate Road, Bradford, BD2 3RJ",
            Phone        = "01274 431000",
            OpeningHours = "Mon–Sun 8:00 AM – 7:00 PM (Apr–Sep), 8:00 AM – 5:00 PM (Oct–Mar)",
            Website      = "https://www.bradford.gov.uk/recycling-and-waste/household-waste-recycling-centres/search-household-waste-sites/",
            Notes        = "Serves north Bradford. Accepts all standard recyclables.",
            Lat = 53.8130, Lng = -1.7390
        },
        new()
        {
            Name         = "Dib Lane HWRC (Oakenshaw)",
            Address      = "Dib Lane, Oakenshaw, Bradford, BD12 7NP",
            Phone        = "01274 431000",
            OpeningHours = "Mon–Sun 8:00 AM – 7:00 PM (Apr–Sep), 8:00 AM – 5:00 PM (Oct–Mar)",
            Website      = "https://www.bradford.gov.uk/recycling-and-waste/household-waste-recycling-centres/search-household-waste-sites/",
            Notes        = "Serves south Bradford and Cleckheaton area.",
            Lat = 53.7500, Lng = -1.7600
        }
    };

    // ─────────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the nearest council offices, libraries, and recycling centres for a
    /// given UK postcode. Distances are calculated using real coordinates from
    /// postcodes.io — falls back to Bradford city-centre coordinates on API failure.
    /// </summary>
    public async Task<NearbyServicesResponse> GetNearbyAsync(string postcode, string type = "all")
    {
        postcode = postcode?.Trim().ToUpperInvariant() ?? "";

        if (string.IsNullOrWhiteSpace(postcode) || !LooksLikeUkPostcode(postcode))
        {
            return new NearbyServicesResponse
            {
                Postcode = postcode,
                Error    = "Please enter a valid UK postcode (e.g. BD3 8PX)."
            };
        }

        // Resolve postcode to real coordinates
        var (userLat, userLng, usingFallback) = await ResolvePostcodeAsync(postcode);

        var result = new NearbyServicesResponse { Postcode = postcode };

        if (usingFallback)
        {
            result.LocationNote =
                "Distances are approximate — postcode lookup is currently unavailable.";
        }

        if (type is "all" or "council_office")
            result.CouncilOffices = CouncilOffices
                .Select(o => ToResult(o, "council_office", userLat, userLng))
                .OrderBy(r => r.EstimatedDistanceMiles)
                .Take(2).ToList();

        if (type is "all" or "library")
            result.Libraries = Libraries
                .Select(o => ToResult(o, "library", userLat, userLng))
                .OrderBy(r => r.EstimatedDistanceMiles)
                .Take(3).ToList();

        if (type is "all" or "recycling_centre")
            result.RecyclingCentres = RecyclingCentres
                .Select(o => ToResult(o, "recycling_centre", userLat, userLng))
                .OrderBy(r => r.EstimatedDistanceMiles)
                .Take(2).ToList();

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Postcode lookup
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Calls https://api.postcodes.io/postcodes/{postcode} and returns the
    /// latitude and longitude.  Returns (FallbackLat, FallbackLng, true) on any
    /// network or parse error so the caller still gets usable (approximate) results.
    /// </summary>
    private async Task<(double lat, double lng, bool usingFallback)> ResolvePostcodeAsync(
        string postcode)
    {
        try
        {
            var client   = _httpClientFactory.CreateClient("postcodes");
            var encoded  = Uri.EscapeDataString(postcode);
            var response = await client.GetAsync($"postcodes/{encoded}");

            if (!response.IsSuccessStatusCode)
                return (FallbackLat, FallbackLng, true);

            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            var root      = doc.RootElement;

            // postcodes.io response: { "status": 200, "result": { "latitude": …, "longitude": … } }
            if (!root.TryGetProperty("result", out var resultEl))
                return (FallbackLat, FallbackLng, true);

            if (!resultEl.TryGetProperty("latitude",  out var latEl) ||
                !resultEl.TryGetProperty("longitude", out var lngEl))
                return (FallbackLat, FallbackLng, true);

            var lat = latEl.GetDouble();
            var lng = lngEl.GetDouble();
            return (lat, lng, false);
        }
        catch
        {
            // Network timeout, JSON parse error, etc. — fall back silently.
            return (FallbackLat, FallbackLng, true);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private static NearbyServiceResult ToResult(
        CouncilLocation loc, string type, double userLat, double userLng)
    {
        return new NearbyServiceResult
        {
            Type                   = type,
            Name                   = loc.Name,
            Address                = loc.Address,
            Phone                  = loc.Phone,
            OpeningHours           = loc.OpeningHours,
            Website                = loc.Website,
            Notes                  = loc.Notes,
            EstimatedDistanceMiles = Haversine(userLat, userLng, loc.Lat, loc.Lng),
            MapUrl = $"https://maps.google.com/?q={Uri.EscapeDataString(loc.Address)}"
        };
    }

    private static double Haversine(double lat1, double lng1, double lat2, double lng2)
    {
        const double R = 3958.8; // Earth radius in miles
        var dLat = ToRad(lat2 - lat1);
        var dLng = ToRad(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2))
              * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return Math.Round(R * c, 1);
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;

    private static bool LooksLikeUkPostcode(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        return System.Text.RegularExpressions.Regex.IsMatch(
            input.Trim().ToUpperInvariant(),
            @"^[A-Z]{1,2}\d[A-Z\d]?\s*\d[A-Z]{2}$");
    }
}
