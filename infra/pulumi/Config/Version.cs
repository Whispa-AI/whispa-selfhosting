namespace Whispa.Aws.Pulumi.Configuration;

/// <summary>
/// Single source of truth for the app image version this infra release deploys.
///
/// The release process bumps <see cref="DefaultImageTag"/> to match the published
/// version (e.g. "0.0.71") so a customer who checks out infra at tag v0.0.71 gets
/// the matching app images with zero extra config. The "0.0.0-dev" placeholder
/// marks an un-released working tree; image refs then fall back to ":latest" so a
/// local checkout still runs (see WhispaConfig.ResolveImageTag).
/// </summary>
public static class BuildInfo
{
    public const string DefaultImageTag = "0.0.0-dev";
}
