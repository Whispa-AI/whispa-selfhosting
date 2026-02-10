# Upgrade Guide

This guide explains how to upgrade your Whispa deployment to new versions.

## Overview

Whispa releases follow [semantic versioning](https://semver.org/):
- **Major** (v2.0.0): Breaking changes, may require migration steps
- **Minor** (v1.2.0): New features, backwards compatible
- **Patch** (v1.1.1): Bug fixes, backwards compatible

## Before Upgrading

### 1. Check Release Notes

Always read the [release notes](https://github.com/Whispa-AI/whispa-selfhosting/releases) before upgrading:
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

### 1. Update Container Images

Edit your `Pulumi.<stack>.yaml`:

```yaml
config:
  whispa:backendImage: ghcr.io/whispa-ai/whispa-backend:v1.2.0
  whispa:frontendImage: ghcr.io/whispa-ai/whispa-frontend:v1.2.0
```

Or set via CLI:
```bash
pulumi config set whispa:backendImage ghcr.io/whispa-ai/whispa-backend:v1.2.0
pulumi config set whispa:frontendImage ghcr.io/whispa-ai/whispa-frontend:v1.2.0
```

### 2. Preview Changes

```bash
pulumi preview
```

Review the changes - typically only ECS task definitions will update.

### 3. Apply Upgrade

```bash
pulumi up
```

This will:
1. Create new ECS task definitions with the new images
2. Deploy new tasks
3. Drain old tasks (zero-downtime if multiple instances)
4. Run database migrations automatically on startup

### 4. Verify Upgrade

```bash
# Check service health
curl https://api.yourcompany.com/health

# Check version (if endpoint exists)
curl https://api.yourcompany.com/version

# Check ECS task status
aws ecs describe-services \
  --cluster whispa-prod \
  --services whispa-prod-backend \
  --query 'services[0].{running:runningCount,desired:desiredCount,status:status}'
```

## Rollback Procedure

If issues occur after upgrade:

### Quick Rollback

```bash
# Revert to previous version
pulumi config set whispa:backendImage ghcr.io/whispa-ai/whispa-backend:v1.1.0
pulumi config set whispa:frontendImage ghcr.io/whispa-ai/whispa-frontend:v1.1.0

# Apply
pulumi up
```

### Database Rollback

If database migrations need to be reverted (rare):

```bash
# Connect to backend container
aws ecs execute-command \
  --cluster whispa-prod \
  --task <task-id> \
  --container backend \
  --interactive \
  --command "/bin/bash"

# Run downgrade
alembic downgrade -1  # Downgrade one version
# or
alembic downgrade <revision>  # Specific revision
```

## Major Version Upgrades

Major versions may require additional steps.

### Pre-Upgrade Checklist

1. Read all release notes between your current and target version
2. Test upgrade in staging environment first
3. Backup database
4. Schedule extended maintenance window
5. Have rollback plan ready

### Example: v1.x to v2.x

```bash
# 1. Backup database
pg_dump -h <rds-endpoint> -U whispa -d whispa > backup-v1.sql

# 2. Update Pulumi configuration (check release notes for new options)
pulumi config set whispa:backendImage ghcr.io/whispa-ai/whispa-backend:v2.0.0
pulumi config set whispa:frontendImage ghcr.io/whispa-ai/whispa-frontend:v2.0.0
pulumi config set whispa:newConfigOption value  # If required

# 3. Preview and apply
pulumi preview
pulumi up

# 4. Run any manual migration steps (from release notes)
aws ecs execute-command \
  --cluster whispa-prod \
  --task <task-id> \
  --container backend \
  --interactive \
  --command "python -m scripts.migrate_v2"

# 5. Verify
curl https://api.yourcompany.com/health
```

## Infrastructure Upgrades

Sometimes infrastructure changes are needed:

### Pulumi Code Updates

```bash
# Pull latest infrastructure code
git pull origin main

# Preview changes
pulumi preview

# Apply (may cause downtime for significant changes)
pulumi up
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

## Automated Upgrades

For organizations wanting automated upgrades:

### Using GitHub Actions

Create `.github/workflows/upgrade.yml`:

```yaml
name: Upgrade Whispa

on:
  workflow_dispatch:
    inputs:
      version:
        description: 'Version to deploy'
        required: true

jobs:
  upgrade:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup Pulumi
        uses: pulumi/actions@v4

      - name: Configure AWS
        uses: aws-actions/configure-aws-credentials@v4
        with:
          aws-access-key-id: ${{ secrets.AWS_ACCESS_KEY_ID }}
          aws-secret-access-key: ${{ secrets.AWS_SECRET_ACCESS_KEY }}
          aws-region: us-east-1

      - name: Upgrade
        run: |
          cd infra/pulumi
          pulumi config set whispa:backendImage ghcr.io/whispa-ai/whispa-backend:${{ inputs.version }}
          pulumi config set whispa:frontendImage ghcr.io/whispa-ai/whispa-frontend:${{ inputs.version }}
          pulumi up --yes
        env:
          PULUMI_ACCESS_TOKEN: ${{ secrets.PULUMI_ACCESS_TOKEN }}
```

## Version Compatibility

| Self-Hosting Repo | Whispa Images | Notes |
|-------------------|---------------|-------|
| v1.0.x | v1.0.0 - v1.x.x | Initial release |
| v1.1.x | v1.0.0 - v1.x.x | Added HA options |

## Getting Help

- Check [TROUBLESHOOTING.md](TROUBLESHOOTING.md) for common issues
- Review [GitHub Issues](https://github.com/Whispa-AI/whispa-selfhosting/issues)
- Contact support@whispa.ai for assistance
