using Conora.Infrastructure.Push;
using Xunit;

namespace Conora.UnitTests;

public class FcmOptionsTests
{
    [Fact]
    public void IsReady_requires_project_and_credentials()
    {
        Assert.False(new FcmOptions().IsReady);
        Assert.False(new FcmOptions { ProjectId = "conora-app" }.IsReady);
        Assert.True(new FcmOptions
        {
            ProjectId = "conora-app",
            CredentialsPath = @"d:\secrets\fcm.json"
        }.IsReady);
        Assert.True(new FcmOptions
        {
            ProjectId = "conora-app",
            CredentialsJson = "{}"
        }.IsReady);
    }
}
