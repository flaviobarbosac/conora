namespace Conora.Domain.Services;

/// <summary>Shared Gemini call cap. The count is the tenant's Gemini audit rows in the last hour.</summary>
public static class GeminiQuota
{
    public const int MaxCallsPerHour = 50;

    public const string HourlyLimitMessage = "Seu limite de chamadas foi consumido. Volte daqui a uma hora.";
}
