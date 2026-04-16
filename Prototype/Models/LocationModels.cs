namespace CouncilChatbotPrototype.Models;

/// <summary>
/// A single nearby council service result (office, library, or recycling centre).
/// </summary>
public class NearbyServiceResult
{
    public string Type         { get; set; } = "";   // "council_office" | "library" | "recycling_centre" | "school"
    public string Name         { get; set; } = "";
    public string Address      { get; set; } = "";
    public string Phone        { get; set; } = "";
    public string OpeningHours { get; set; } = "";
    public string Website      { get; set; } = "";
    public string Notes        { get; set; } = "";

    /// <summary>FUTURE INTEGRATION POINT: replace with real distance calculation via Postcodes.io + Haversine.</summary>
    public double EstimatedDistanceMiles { get; set; } = 0;

    /// <summary>FUTURE INTEGRATION POINT: Populate with a real Google Maps / Leaflet deep-link.</summary>
    public string MapUrl { get; set; } = "";
}

/// <summary>
/// Full response from /api/location/nearby — contains results grouped by service type.
/// </summary>
public class NearbyServicesResponse
{
    public string Postcode                      { get; set; } = "";
    public List<NearbyServiceResult> CouncilOffices   { get; set; } = new();
    public List<NearbyServiceResult> Libraries        { get; set; } = new();
    public List<NearbyServiceResult> RecyclingCentres { get; set; } = new();
    public List<NearbyServiceResult> Schools          { get; set; } = new();
    public string Error                         { get; set; } = "";

    /// <summary>
    /// Set when the postcode API was unavailable and Bradford city-centre fallback
    /// coordinates were used. Results are still returned — distances are approximate.
    /// </summary>
    public string LocationNote                  { get; set; } = "";
}

/// <summary>
/// Internal data record used by LocationService to hold a Bradford location's details.
/// </summary>
public class CouncilLocation
{
    public string Name         { get; set; } = "";
    public string Address      { get; set; } = "";
    public string Phone        { get; set; } = "";
    public string OpeningHours { get; set; } = "";
    public string Website      { get; set; } = "";
    public string Notes        { get; set; } = "";
    public double Lat          { get; set; }
    public double Lng          { get; set; }
}
