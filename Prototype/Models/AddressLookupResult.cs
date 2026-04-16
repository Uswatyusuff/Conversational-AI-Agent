namespace CouncilChatbotPrototype.Models;

public class AddressLookupResult
{
    public string Postcode { get; set; } = "";
    public List<string> Addresses { get; set; } = new();
    public string Error { get; set; } = "";
}