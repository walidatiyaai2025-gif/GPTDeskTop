using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class ApplicationBuildIdentityTests
{
    [Fact]
    public void ParseStableBuildId_ExtractsStampedSourcePrefix()
    {
        var buildId = ApplicationBuildIdentity.ParseStableBuildId("1.8.0+stable.A1B2C3D4");

        Assert.Equal("a1b2c3d4", buildId);
    }

    [Fact]
    public void ParseStableBuildId_IgnoresAdditionalSourceRevisionMetadata()
    {
        var buildId = ApplicationBuildIdentity.ParseStableBuildId(
            "1.8.0+stable.abcdef12.0123456789abcdef0123456789abcdef01234567");

        Assert.Equal("abcdef12", buildId);
    }

    [Fact]
    public void ParseStableBuildId_DoesNotTreatOrdinaryInformationalMetadataAsStable()
    {
        Assert.Null(ApplicationBuildIdentity.ParseStableBuildId("1.8.0+0123456789abcdef"));
        Assert.Null(ApplicationBuildIdentity.ParseStableBuildId("1.8.0+stable.123"));
        Assert.Null(ApplicationBuildIdentity.ParseStableBuildId(null));
    }

    [Fact]
    public void FormatDisplayVersion_AddsStableIdentityOnlyWhenPresent()
    {
        Assert.Equal("v1.8.0", ApplicationBuildIdentity.FormatDisplayVersion("1.8.0", null));
        Assert.Equal(
            "v1.8.0 • stable abcdef12",
            ApplicationBuildIdentity.FormatDisplayVersion("1.8.0", "ABCDEF12"));
    }
}
