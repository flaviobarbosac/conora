namespace Conora.Infrastructure.Push;

public sealed class FcmOptions
{
    public const string SectionName = "Fcm";

    /// <summary>Firebase project id (same as in the service account JSON).</summary>
    public string ProjectId { get; set; } = "";

    /// <summary>Raw service-account JSON. Prefer secrets/env over committing the file.</summary>
    public string CredentialsJson { get; set; } = "";

    /// <summary>Optional path to a service-account JSON file (local/dev).</summary>
    public string CredentialsPath { get; set; } = "";

    public bool IsReady =>
        !string.IsNullOrWhiteSpace(ProjectId)
        && (!string.IsNullOrWhiteSpace(CredentialsJson) || !string.IsNullOrWhiteSpace(CredentialsPath));
}
