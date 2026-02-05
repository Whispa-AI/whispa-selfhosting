# Deployment Guide

This guide walks you through deploying Whispa to your AWS account.

## Overview

The deployment process:
1. Install prerequisites and configure Pulumi
2. Set up DNS (Route53 recommended)
3. Configure your stack
4. Deploy with `pulumi up`
5. Verify the deployment

**Estimated time**: 30-45 minutes

## Step 1: Install Prerequisites

Ensure you have all tools installed (see [PREREQUISITES.md](PREREQUISITES.md)):

```bash
# Verify installations
pulumi version      # Should be v3.x.x+
dotnet --version    # Should be 8.x.x
aws sts get-caller-identity  # Should show your AWS account
```

## Step 2: Set Up Pulumi Backend

Pulumi needs somewhere to store state. Choose one:

### Option A: S3 Backend (Recommended for AWS)

```bash
# Create S3 bucket for state
aws s3 mb s3://your-company-pulumi-state --region your-region

# Login with S3 backend
pulumi login s3://your-company-pulumi-state?region=your-region
```

### Option B: Pulumi Cloud (Free tier available)

```bash
pulumi login  # Opens browser to sign in
```

### Option C: Local (Testing only)

```bash
pulumi login --local
```

## Step 3: Set Up DNS with Route53

We recommend using Route53 for automatic DNS management. If your domain is hosted elsewhere (GoDaddy, Namecheap, etc.), you can delegate a subdomain to Route53.

### Delegating a Subdomain to Route53

Example: Delegate `whispa.yourcompany.com` to Route53 while keeping `yourcompany.com` on your current provider.

```bash
# 1. Create hosted zone in Route53
aws route53 create-hosted-zone \
  --name whispa.yourcompany.com \
  --caller-reference "whispa-$(date +%s)"
```

Note the **Hosted Zone ID** (e.g., `Z0123456789ABC`) and **Nameservers** from the output.

```bash
# 2. Get nameservers (if you missed them)
aws route53 get-hosted-zone \
  --id Z0123456789ABC \
  --query 'DelegationSet.NameServers' \
  --output table
```

**3. Add NS records at your DNS provider:**

In your current DNS provider (GoDaddy, etc.), add NS records:

| Type | Name | Value |
|------|------|-------|
| NS | whispa | ns-123.awsdns-12.com |
| NS | whispa | ns-456.awsdns-34.net |
| NS | whispa | ns-789.awsdns-56.org |
| NS | whispa | ns-012.awsdns-78.co.uk |

This delegates `whispa.yourcompany.com` to Route53.

## Step 4: Create ACM Certificate

If using Route53 with `hostedZoneId` configured, Pulumi can auto-provision certificates. Otherwise, create one manually:

1. Go to AWS Console → ACM → Request Certificate
2. Add domain names:
   - `whispa.yourcompany.com`
   - `*.whispa.yourcompany.com` (for API subdomain)
3. Choose DNS validation
4. Add the CNAME records shown to your DNS
5. Wait for validation (can take up to 30 minutes)
6. Note the **Certificate ARN**

## Step 5: Verify SES Email

For sending password reset and notification emails:

1. Go to AWS Console → SES → Verified Identities
2. Create identity → Email address (or domain)
3. Verify via the confirmation email
4. Note the verified email address for `mailFrom` config

## Step 6: Configure Pulumi Stack

```bash
cd infra/pulumi
cp Pulumi.dev.yaml.example Pulumi.prod.yaml
pulumi stack init prod
```

### Required Configuration

```bash
# AWS Region
pulumi config set aws:region ap-southeast-2

# Domain settings
pulumi config set whispa:domainName whispa.yourcompany.com
pulumi config set whispa:frontendUrl https://whispa.yourcompany.com
pulumi config set whispa:apiDomainName api.whispa.yourcompany.com

# SSL Certificate (if not using auto-provisioning)
pulumi config set whispa:certificateArn "arn:aws:acm:..."

# Route53 (enables automatic DNS)
pulumi config set whispa:hostedZoneId "Z0123456789ABC"

# Email
pulumi config set whispa:mailFrom noreply@yourcompany.com

# LLM API Key (encrypted)
pulumi config set --secret whispa:llmApiKey "sk-or-your-openrouter-key"

# Transcription - AWS Transcribe recommended (no API key needed)
pulumi config set whispa:transcriptionProvider amazon
```

### Bootstrap Admin User (Strongly Recommended)

> ⚠️ **Do this now!** The admin user is created during the initial database migration. If you skip this, you'll need to manually promote a user via CLI later (see [Creating Admin User After Deployment](#creating-admin-user-after-deployment)).

```bash
pulumi config set whispa:superuserEmail admin@yourcompany.com
pulumi config set --secret whispa:superuserPassword "your-secure-password"
```

This creates a fully activated admin user (`is_active`, `is_verified`, `is_superuser` all set to `true`) on first deployment.

### Optional Configuration

```bash
# Sentry error tracking
pulumi config set whispa:sentryDsn "https://xxx@sentry.io/xxx"

# Resource sizing (defaults are fine for small deployments)
pulumi config set whispa:dbInstanceClass db.t3.medium
pulumi config set whispa:backendCpu 512
pulumi config set whispa:backendMemory 1024
```

## Step 7: Preview and Deploy

```bash
# Preview what will be created
pulumi preview

# Deploy (takes 15-20 minutes)
pulumi up
```

Type `yes` when prompted to confirm.

### What Gets Created

- VPC with public/private subnets
- NAT Gateway for outbound traffic
- RDS PostgreSQL database
- S3 bucket for audio storage
- ECS Fargate cluster with backend and frontend services
- Application Load Balancer with SSL
- IAM roles and policies
- Secrets in AWS Secrets Manager
- CloudWatch log groups
- Route53 DNS records (if configured)

## Step 8: Verify Deployment

### Check Outputs

```bash
pulumi stack output
```

### Check ECS Services

```bash
aws ecs describe-services \
  --cluster whispa-prod \
  --services whispa-prod-backend whispa-prod-frontend \
  --query 'services[*].{name:serviceName,status:status,running:runningCount}'
```

### Check Application Health

```bash
curl https://whispa.yourcompany.com/health
# Expected: {"status": "healthy"}
```

### Access the Application

- **Frontend:** https://whispa.yourcompany.com
- **API Docs:** https://api.whispa.yourcompany.com/docs

## Creating Admin User After Deployment

> **Recommended:** Set `superuserEmail` and `superuserPassword` during initial setup (Step 6) to avoid this manual step. The admin user is created automatically during the first database migration.

If you didn't set `superuserEmail`/`superuserPassword` before deployment:

### Option 1: Register + Promote (Recommended)

1. Register a user through the UI (email verification not required)
2. Promote them to admin via ECS Exec:

```bash
# Get task ID
TASK_ID=$(aws ecs list-tasks \
  --cluster whispa-prod \
  --service-name whispa-prod-backend \
  --query 'taskArns[0]' \
  --output text | rev | cut -d'/' -f1 | rev)

# Promote user to admin (also sets is_active and is_verified)
aws ecs execute-command \
  --cluster whispa-prod \
  --task $TASK_ID \
  --container backend \
  --interactive \
  --command "python -m commands.set_admin_role your@email.com"
```

The `set_admin_role` command will:
- Set role to `admin`
- Set `is_superuser` to `true`
- Set `is_active` to `true` (no email verification needed)
- Set `is_verified` to `true`

### Option 2: Add Config and Redeploy

```bash
pulumi config set whispa:superuserEmail admin@yourcompany.com
pulumi config set --secret whispa:superuserPassword "your-secure-password"
pulumi up

# Then trigger a migration re-run by restarting the backend
aws ecs update-service --cluster whispa-prod --service whispa-prod-backend --force-new-deployment
```

## Troubleshooting

### Secrets Already Exist Error

If you see "secret is already scheduled for deletion":

```bash
aws secretsmanager delete-secret \
  --secret-id whispa/prod/app-secrets \
  --force-delete-without-recovery \
  --region your-region
```

Repeat for any other secrets mentioned in the error.

### ECS Tasks Failing to Start

Check the logs:

```bash
aws logs tail /ecs/whispa-prod-backend --follow
```

Common issues:
- **Container image pull error:** Ensure images are public or configure registry credentials
- **Health check failing:** Check the backend logs for startup errors
- **Database connection:** Verify security groups allow ECS → RDS traffic

### DNS Not Resolving

1. Check NS delegation: `dig NS whispa.yourcompany.com`
2. Verify Route53 records: `aws route53 list-resource-record-sets --hosted-zone-id Z0123...`
3. Wait for propagation (can take 5-10 minutes)

### Certificate Validation Stuck

- Ensure CNAME records are added for DNS validation
- Check ACM console for validation status
- If using Route53, ensure the hosted zone ID is correct

See [TROUBLESHOOTING.md](TROUBLESHOOTING.md) for more solutions.

## Updating Configuration

After initial deployment, you can update configuration:

```bash
pulumi config set whispa:desiredCount 2
pulumi up
```

ECS services will be redeployed with the new settings.

## Destroying the Deployment

To remove all resources:

```bash
pulumi destroy
```

**Warning:** This deletes all data including the database. Export any data you need first.

## Seeding the Database

After deployment, seed the database with default scenarios, workflows, and prompts:

```bash
# Connect to the backend container
TASK_ID=$(aws ecs list-tasks --cluster whispa-prod --service-name whispa-prod-backend --region ap-southeast-2 --query 'taskArns[0]' --output text | rev | cut -d'/' -f1 | rev)

aws ecs execute-command \
  --cluster whispa-prod \
  --task $TASK_ID \
  --container backend \
  --region ap-southeast-2 \
  --interactive \
  --command "/bin/bash"
```

Then inside the container:

```bash
# Seed all data (creates or updates existing)
python -m seeds.seed_all

# Or clear and reseed from scratch
python -m seeds.seed_all --clear
```

This seeds:
- Scenario definitions
- Workflow step configurations
- Prompt templates

## Common ECS Commands

### Connect to Backend Container

```bash
TASK_ID=$(aws ecs list-tasks --cluster whispa-prod --service-name whispa-prod-backend --region ap-southeast-2 --query 'taskArns[0]' --output text | rev | cut -d'/' -f1 | rev)

aws ecs execute-command \
  --cluster whispa-prod \
  --task $TASK_ID \
  --container backend \
  --region ap-southeast-2 \
  --interactive \
  --command "/bin/bash"
```

### View Logs

```bash
aws logs tail /ecs/whispa-prod-backend --follow --region ap-southeast-2
```

### Force Restart Services

```bash
# Restart backend (e.g., after IAM policy changes)
aws ecs update-service --cluster whispa-prod --service whispa-prod-backend --force-new-deployment --region ap-southeast-2

# Restart frontend
aws ecs update-service --cluster whispa-prod --service whispa-prod-frontend --force-new-deployment --region ap-southeast-2
```

### Check Service Status

```bash
aws ecs describe-services \
  --cluster whispa-prod \
  --services whispa-prod-backend whispa-prod-frontend \
  --query 'services[*].{name:serviceName,status:status,running:runningCount,desired:desiredCount}' \
  --region ap-southeast-2
```

## Next Steps

- [CONFIGURATION.md](CONFIGURATION.md) - All configuration options
- [SECURITY.md](SECURITY.md) - Security best practices
- [UPGRADES.md](UPGRADES.md) - Upgrading to new versions
