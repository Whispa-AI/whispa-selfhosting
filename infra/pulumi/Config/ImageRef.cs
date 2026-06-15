namespace Whispa.Aws.Pulumi.Configuration;

/// <summary>
/// Pure app-image-reference resolution. Deliberately free of Pulumi types so it is
/// unit-testable without a stack/runtime (see infra/pulumi.tests).
///
/// Precedence (highest first):
///   1. an explicit full ref (whispa:backendImage / whispa:frontendImage)
///   2. whispa:imageTag + the registry
///   3. the version baked into this infra release (BuildInfo.DefaultImageTag)
///
/// The unreleased <see cref="DevPlaceholderTag"/> resolves to ":latest" so an
/// un-stamped working tree still pulls a runnable image.
/// </summary>
public static class ImageRef
{
    /// <summary>Sentinel meaning "this working tree has not been stamped by a release".</summary>
    public const string DevPlaceholderTag = "0.0.0-dev";

    public static string Resolve(
        string? fullRefOverride,
        string? imageTagOverride,
        string registry,
        string repository,
        string defaultTag)
    {
        if (!string.IsNullOrWhiteSpace(fullRefOverride))
            return fullRefOverride.Trim();

        var tag = !string.IsNullOrWhiteSpace(imageTagOverride)
            ? imageTagOverride.Trim()
            : defaultTag;

        if (tag == DevPlaceholderTag)
            tag = "latest";

        return $"{registry}/{repository}:{tag}";
    }
}
