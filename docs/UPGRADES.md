# Upgrade Guide

This guide explains how to upgrade your Whispa deployment to new versions.

## Overview

Whispa releases follow [semantic versioning](https://semver.org/):
- **Major** (v2.0.0): Breaking changes, may require migration steps
- **Minor** (v1.2.0): New features, backwards compatible
- **Patch** (v1.1.1): Bug fixes, backwards compatible

## How versioning works here (lockstep)

This infra repo and the Whispa app images move **in lockstep**:

- Each release is a git tag `vX.Y.Z`. That release builds the matching app images
  `ghcr.io/whispa-ai/whispa-backend:X.Y.Z` and `…/whispa-frontend:X.Y.Z`.
- The version is baked into the infra at release time
  (`infra/pulumi/Config/Version.cs` → `BuildInfo.DefaultImageTag`). So if you
  **check out tag `vX.Y.Z` and deploy, you get app `X.Y.Z` with zero extra config.**
- To deploy a *different* app version than the checkout's default, set
  `whispa:imageTag` (a single key both services use).

**Image resolution precedence** (highest first):

1. `whispa:backendImage` / `whispa:frontendImage` — full image refs (advanced)
2. `whispa:imageTag` — the simple "pin a version" knob
3. the version baked into the infra release (`Config/Version.cs`)

Available versions are listed in [CHANGELOG.md](../CHANGELOG.md) and on the
[Releases page](https://github.com/Whispa-AI/whispa-selfhosting/releases). See
[CONFIGURATION.md](CONFIGURATION.md#app-version-container-images) for the full key
reference.

## Before Upgrading

### 1. Check Release Notes

Always read the [release notes](https://github.com/Whispa-AI/whispa-selfhosting/releases)
and [CHANGELOG.md](../CHANGELOG.md) before upgrading:
- Breaking changes
- New configuration options
- Migration requirements

### 2. Backup Database

```bash
# Using pg_dump (connect to RDS)
pg_dump -h <rds-endpoint> -U whispa -d whispa > backup-$(date +%Y%m%d).sql

# Or use AWS RDS snapshot
aws rds create-db-snapshot \
  --db-instance-identifier whispa-prod-db \
  --db-snapshot-identifier whispa-backup-$(date +%Y%m%d)
```

### 3. Plan Maintenance Window

Upgrades typically cause a brief service interruption (30 seconds to 2 minutes).
Schedule upgrades during low-traffic periods.

## Standard Upgrade Process

### Option A: GitHub Actions (recommended)

If you've set up the [deploy pipeline](CI-CD.md), upgrading is a button:

1. **Actions → Deploy Whispa → Run workflow**
2. Choose the environment (`dev` / `test`)
3. Enter the new version, e.g. `0.0.89`
4. **Run workflow**

It runs `pulumi up` with `whispa:imageTag` set to the version you entered. See
[CI-CD.md](CI-CD.md) for setup and details.

### Option B: From your laptop

Two equivalent ways — move the whole checkout to a new release (keeps infra and app
in lockstep), or just pin the app version:

```bash
# B1 — move to a new release (recommended: infra + app together)
git fetch --tags
git checkout vX.Y.Z
cd infra/pulumi
pulumi up            # deploys app X.Y.Z by default

# B2 — pin only the app version on your current infra
cd infra/pulumi
pulumi config set whispa:imageTag X.Y.Z
pulumi up
```

`pulumi up` will:
1. Create new ECS task definitions with the new images
2. Deploy new tasks
3. Drain old tasks (zero-downtime if multiple instances)
4. Run database migrations automatically on startup

### Verify Upgrade

```bash
# Check service health
curl https://api.yourcompany.com/health

# Check ECS task status
aws ecs describe-services \
  --cluster whispa-prod \
  --services whispa-prod-backend \
  --query 'services[0].{running:runningCount,desired:desiredCount,status:status}'
```

## Rollback Procedure

### Quick Rollback

Re-deploy the previous good version — via the workflow (enter the older version) or
locally:

```bash
# Pin the previous version and apply
pulumi config set whispa:imageTag X.Y.(Z-1)
pulumi up
```

ECS rolls task definitions back to that image.

### Database Rollback

Migrations are applied on startup and are **forward-only** by design — rolling the
app image back does not roll back a migration. Only revert a migration if a release
note says it's safe:

```bash
# Connect to backend container
aws ecs execute-command \
  --cluster whispa-prod \
  --task <task-id> \
  --container backend \
  --interactive \
  --command "/bin/bash"

# Run downgrade
alembic downgrade -1            # one revision
# or
alembic downgrade <revision>    # a specific revision
```

> If you may need to cross a migration boundary back and forth, restore from the
> snapshot you took in [Before Upgrading](#2-backup-database) instead.

## Major Version Upgrades

Major versions may require additional steps.

### Pre-Upgrade Checklist

1. Read all release notes between your current and target version
2. Test the upgrade in your `dev`/`test` environment first
3. Backup database
4. Schedule an extended maintenance window
5. Have a rollback plan ready

### Example: v1.x to v2.x

```bash
# 1. Backup database
pg_dump -h <rds-endpoint> -U whispa -d whispa > backup-v1.sql

# 2. Move to the new release and add any new required config (from release notes)
git fetch --tags && git checkout v2.0.0
cd infra/pulumi
pulumi config set whispa:newConfigOption value   # only if a release note requires it

# 3. Preview and apply
pulumi preview
pulumi up

# 4. Verify
curl https://api.yourcompany.com/health
```

## Infrastructure Upgrades

Most releases are app-only (just a new image), but some also change infrastructure
(new IAM permissions, networking, alarms, etc.). Because infra is **versioned with
the same tags**, you pick those changes up by checking out a newer tag:

```bash
git fetch --tags
git checkout vX.Y.Z
cd infra/pulumi
pulumi preview        # review infra changes
pulumi up             # apply (may cause downtime for significant changes)
```

### RDS Upgrades

For database engine upgrades:

```bash
# Set new engine version in config
pulumi config set whispa:dbEngineVersion 15.4

# Apply (causes downtime)
pulumi up
```

**Note**: RDS engine upgrades cause 10-30 minutes of downtime.

## Version Compatibility

Infra and app images are released together and are designed to be deployed at the
**same version** — that's the default when you check out a release tag. The
`whispa:imageTag` override exists for short-lived skew (e.g. hotfixing the app
version on stable infra), not as a long-term mixed-version mode. When in doubt,
match the app version to the infra tag you checked out.

## Getting Help

- Check [TROUBLESHOOTING.md](TROUBLESHOOTING.md) for common issues
- Review [GitHub Issues](https://github.com/Whispa-AI/whispa-selfhosting/issues)
- Contact support@whispa.ai for assistance
