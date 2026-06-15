#!/usr/bin/env bash
#
# prepare-release.sh — stamp the infra release version.
#
# Run during release prep (locally, or from the publish CI). It:
#   1. stamps BuildInfo.DefaultImageTag in infra/pulumi/Config/Version.cs, so a
#      customer who checks out this release deploys the matching app images with
#      no extra config; and
#   2. finalizes the CHANGELOG, moving everything under "## [Unreleased]" into a
#      new "## [<version>] - <date>" section.
#
# It does NOT commit, tag, or push — the caller reviews the diff, then tags.
#
# Usage:
#   scripts/prepare-release.sh 0.0.72
#   scripts/prepare-release.sh v0.0.72   # leading v is stripped
#
set -euo pipefail

VERSION="${1:?usage: prepare-release.sh <version, e.g. 0.0.72>}"
VERSION="${VERSION#v}"

if ! printf '%s' "$VERSION" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.-]+)?$'; then
  echo "error: '$VERSION' does not look like a semver version (e.g. 0.0.72)" >&2
  exit 1
fi

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION_FILE="$ROOT/infra/pulumi/Config/Version.cs"
CHANGELOG="$ROOT/CHANGELOG.md"
TODAY="$(date +%F)"

# 1. Stamp the default image tag (must be a plain string literal in Version.cs).
if ! grep -Eq 'public const string DefaultImageTag = "[^"]*";' "$VERSION_FILE"; then
  echo "error: could not find DefaultImageTag literal in $VERSION_FILE" >&2
  exit 1
fi
sed -i.bak -E "s/(public const string DefaultImageTag = )\"[^\"]*\";/\1\"${VERSION}\";/" "$VERSION_FILE"
rm -f "$VERSION_FILE.bak"
echo "stamped DefaultImageTag = \"$VERSION\""

# 2. Finalize the CHANGELOG: insert a dated version heading just under [Unreleased].
if ! grep -q '^## \[Unreleased\]' "$CHANGELOG"; then
  echo "error: no '## [Unreleased]' section in $CHANGELOG" >&2
  exit 1
fi
awk -v ver="$VERSION" -v date="$TODAY" '
  /^## \[Unreleased\]/ && !done {
    print; print ""; print "## [" ver "] - " date; done=1; next
  }
  { print }
' "$CHANGELOG" > "$CHANGELOG.tmp" && mv "$CHANGELOG.tmp" "$CHANGELOG"
echo "added CHANGELOG section [$VERSION] - $TODAY"

echo
echo "Done. Review the diff, then tag v$VERSION:"
echo "  git diff"
echo "  git commit -am 'chore(release): v$VERSION' && git tag v$VERSION"
