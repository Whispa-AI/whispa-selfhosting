using Whispa.Aws.Pulumi.Components;
using Xunit;

namespace Whispa.Aws.Pulumi.Tests;

/// <summary>
/// Physical names for load balancers and target groups are capped at 32
/// characters, may contain only letters, digits and hyphens, must not start or
/// end with a hyphen, and must be unique per account and region. Getting this
/// wrong fails at deploy time or, worse, collides with another stack's
/// resources — so the rules are pinned here.
/// </summary>
public class MediaIngressNamingTests
{
    private static string Name(string prefix, string suffix, int max = 32) =>
        MediaIngressStack.PhysicalName(prefix, suffix, max);

    [Fact]
    public void ShortNamesArePassedThroughUnchanged()
    {
        Assert.Equal("whispa-cc-test-media", Name("whispa-cc-test", "media"));
    }

    [Fact]
    public void LongNamesAreTruncatedWithinTheLimit()
    {
        var name = Name("whispa-a-very-long-customer-environment", "m42010");
        Assert.True(name.Length <= 32, $"'{name}' is {name.Length} chars");
    }

    [Fact]
    public void PrefixesSharingATailDoNotCollide()
    {
        // Truncating the front alone would map these to the same physical name.
        var first = Name("acme-corporation-australia-prod", "m42010");
        var second = Name("beta-corporation-australia-prod", "m42010");
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void DifferentPortsGetDifferentNames()
    {
        Assert.NotEqual(
            Name("whispa-cc-test", "m42010"), Name("whispa-cc-test", "m42011"));
    }

    [Fact]
    public void NamesAreStableAcrossCalls()
    {
        // A per-process hash would make every run propose a replacement.
        Assert.Equal(
            Name("whispa-a-very-long-customer-environment", "m42010"),
            Name("whispa-a-very-long-customer-environment", "m42010"));
    }

    [Theory]
    [InlineData("whispa_dev.au", "media")]
    [InlineData("--leading", "media")]
    [InlineData("trailing--", "media")]
    [InlineData("Mixed_Case.Stack", "m42010")]
    public void NamesAreAlwaysValidForAws(string prefix, string suffix)
    {
        var name = Name(prefix, suffix);

        Assert.InRange(name.Length, 1, 32);
        Assert.DoesNotContain("--", name);
        Assert.False(name.StartsWith('-'), $"'{name}' starts with a hyphen");
        Assert.False(name.EndsWith('-'), $"'{name}' ends with a hyphen");
        Assert.All(name, c => Assert.True(
            char.IsAsciiLetterOrDigit(c) || c == '-', $"'{name}' has invalid char '{c}'"));
    }

}
