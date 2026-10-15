namespace Conora.Domain.Entities;

/// <summary>Operational failure counted by the console. No actor, message or payload.</summary>
public class RequestFailure : ModelBase
{
    public int StatusCode { get; private set; }
    public string Title { get; private set; } = "";
    public string Area { get; private set; } = "";
    public DateTime OccurredAt { get; private set; }

    private RequestFailure()
    {
    }

    public static RequestFailure Record(int statusCode, string title, string area) => new()
    {
        StatusCode = statusCode,
        Title = Clip(title, 80),
        Area = Clip(area, 40),
        OccurredAt = DateTime.UtcNow
    };

    private static string Clip(string? value, int max)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "outro" : value.Trim();
        return text.Length <= max ? text : text[..max];
    }
}
