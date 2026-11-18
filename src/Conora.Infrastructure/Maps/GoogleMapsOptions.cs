namespace Conora.Infrastructure.Maps;

/// <summary>Browser key for Maps JavaScript and Geocoding. Empty hides the map. Never written to the user table.</summary>
public sealed class GoogleMapsOptions
{
    public const string SectionName = "GoogleMaps";

    public string ApiKey { get; set; } = "";
}
