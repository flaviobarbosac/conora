using Xunit;

namespace Conora.UnitTests;

public class PushEndpointsContractTests
{
    [Fact]
    public void Push_endpoints_file_exposes_status_tokens_and_test_routes()
    {
        var source = File.ReadAllText(FindPushEndpoints());
        Assert.Contains("MapGroup(\"/push\")", source);
        Assert.Contains("MapGet(\"/status\"", source);
        Assert.Contains("MapGet(\"/tokens\"", source);
        Assert.Contains("MapPost(\"/tokens\"", source);
        Assert.Contains("MapPost(\"/tokens/unregister\"", source);
        Assert.Contains("MapPost(\"/test\"", source);
        Assert.Contains("MapPost(\"/send\"", source);
        Assert.Contains("RequireAuthorization()", source);
    }

    private static string FindPushEndpoints()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Conora.Api", "Endpoints", "PushEndpoints.cs");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("PushEndpoints.cs não encontrado.");
    }
}
