namespace Whispa.Aws.Pulumi.Configuration;

/// <summary>
/// Single source of truth for the app image version this infra release deploys.
///
/// scripts/prepare-release.sh bumps <see cref="DefaultImageTag"/> to match the
/// published version (e.g. "0.0.71") so a customer who checks out infra at tag
/// v0.0.71 gets the matching app images with zero extra config. Must stay a plain
/// string literal so the release script can stamp it. The value equals
/// <see cref="ImageRef.DevPlaceholderTag"/> ("0.0.0-dev") on an un-released working
/// tree, which <see cref="ImageRef.Resolve"/> maps to ":latest".
/// </summary>
public static class BuildInfo
{
    public const string DefaultImageTag = "0.0.116";
}
