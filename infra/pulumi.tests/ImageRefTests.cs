using Whispa.Aws.Pulumi.Configuration;
using Xunit;

namespace Whispa.Aws.Pulumi.Tests;

/// <summary>
/// Exercises the app-image precedence rules end-to-end (without a Pulumi runtime).
/// Together these pin the version-resolution contract the release pipeline relies on.
/// </summary>
public class ImageRefTests
{
    private const string Registry = "ghcr.io/whispa-ai";

    [Fact]
    public void FullRef_Override_Wins_Over_Everything()
    {
        var result = ImageRef.Resolve(
            fullRefOverride: "ghcr.io/acme/whispa-backend:custom",
            imageTagOverride: "0.0.99",
            registry: Registry, repository: "whispa-backend", defaultTag: "0.0.71");

        Assert.Equal("ghcr.io/acme/whispa-backend:custom", result);
    }

    [Fact]
    public void ImageTag_Used_When_No_FullRef()
    {
        var result = ImageRef.Resolve(
            fullRefOverride: null, imageTagOverride: "0.0.80",
            registry: Registry, repository: "whispa-backend", defaultTag: "0.0.71");

        Assert.Equal("ghcr.io/whispa-ai/whispa-backend:0.0.80", result);
    }

    [Fact]
    public void StampedDefault_FlowsThrough_When_Nothing_Overridden()
    {
        // THE Phase 3 guarantee: prepare-release.sh stamps DefaultImageTag, and a
        // customer who sets nothing deploys exactly that version.
        var result = ImageRef.Resolve(
            fullRefOverride: null, imageTagOverride: null,
            registry: Registry, repository: "whispa-backend", defaultTag: "0.0.72");

        Assert.Equal("ghcr.io/whispa-ai/whispa-backend:0.0.72", result);
    }

    [Fact]
    public void DevPlaceholder_Resolves_To_Latest()
    {
        var result = ImageRef.Resolve(
            fullRefOverride: null, imageTagOverride: null,
            registry: Registry, repository: "whispa-frontend",
            defaultTag: ImageRef.DevPlaceholderTag);

        Assert.Equal("ghcr.io/whispa-ai/whispa-frontend:latest", result);
    }

    [Fact]
    public void Backcompat_UnreleasedDefault_Equals_OldHardcodedDefault()
    {
        // Proves the Phase 1 no-op claim: on an un-stamped tree with nothing set,
        // the derived ref equals the previous hardcoded "...:latest".
        var backend = ImageRef.Resolve(null, null, Registry, "whispa-backend", BuildInfo.DefaultImageTag);
        var frontend = ImageRef.Resolve(null, null, Registry, "whispa-frontend", BuildInfo.DefaultImageTag);

        Assert.Equal("ghcr.io/whispa-ai/whispa-backend:latest", backend);
        Assert.Equal("ghcr.io/whispa-ai/whispa-frontend:latest", frontend);
    }

    [Theory]
    [InlineData("   ", "0.0.71", "ghcr.io/whispa-ai/whispa-backend:0.0.71")] // blank full ref ignored
    [InlineData(null, "   ", "ghcr.io/whispa-ai/whispa-backend:latest")]     // blank imageTag → dev default → latest
    public void Blank_Inputs_Are_Treated_As_Unset(string? fullRef, string? imageTag, string expected)
    {
        var result = ImageRef.Resolve(
            fullRef, imageTag, Registry, "whispa-backend", ImageRef.DevPlaceholderTag);

        Assert.Equal(expected, result);
    }
}
