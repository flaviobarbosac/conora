using System.Diagnostics;

namespace Conora.Services.Common;

public static class DomainActivitySource
{
    public const string Name = "Conora.Domain";
    public static readonly ActivitySource Instance = new(Name, "1.0.0");
}
