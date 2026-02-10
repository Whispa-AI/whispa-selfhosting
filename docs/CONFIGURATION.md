# Configuration Reference

This document describes all configuration options for your Whispa deployment.

## How Configuration Works

Pulumi configuration lives in a `Pulumi.<stack>.yaml` file. **You can edit this file directly** instead of running `pulumi config set` for each value. The only exception is **secrets** (API keys, passwords), which must be set via the CLI so Pulumi can encrypt them:

```bash
pulumi config set --secret whispa:llmApiKey "sk-or-..."
```

To get started quickly, copy the example file:

```bash
cd infra/pulumi
cp Pulumi.dev.yaml.example Pulumi.<your-stack>.yaml
# Edit the file with your values, then set secrets:
pulumi config set --secret whispa:llmApiKey "your-key"
```

## Required Configuration

| Key | Description | Example |
|-----|-------------|---------|
| `aws:region` | AWS region for deployment | `us-east-1` |
| `whispa:domainName` | Your domain for Whispa | `whispa.company.com` |
| `whispa:frontendUrl` | Full frontend URL | `https://whispa.company.com` |
| `whispa:mailFrom` | Verified SES sender email | `noreply@company.com` |
| `whispa:llmApiKey` | OpenRouter/LLM API key | (secret) |

You must also provide either `whispa:certificateArn` OR enable `whispa:autoCertificate` (with `whispa:hostedZoneId`).

## Resource Naming

Control how AWS resources are named:

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:resourcePrefix` | (none) | Custom prefix for all resources (e.g., `acme-prod`) |
| `whispa:projectName` | `whispa` | Project name (used if resourcePrefix not set) |
| `whispa:environment` | Stack name | Environment name (used if resourcePrefix not set) |

**Resource naming behavior:**
- If `resourcePrefix` is set: `{resourcePrefix}-{resourceType}` (e.g., `acme-prod-alb`)
- If not set: `{projectName}-{environment}-{resourceType}` (e.g., `whispa-prod-alb`)

## Networking

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:vpcCidr` | `10.0.0.0/16` | VPC CIDR block (must be /20 or larger) |

The deployment always creates 2 AZs, a NAT Gateway, and public/private subnets.

## Database (RDS)

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:dbInstanceClass` | `db.t3.medium` | RDS instance type |
| `whispa:dbAllocatedStorage` | `20` | Storage in GB |
| `whispa:dbName` | `whispa` | Database name |
| `whispa:dbUsername` | `whispa_admin` | Database username |
| `whispa:dbBackupRetentionDays` | `7` | Backup retention in days |
| `whispa:dbMultiAz` | `false` | Enable Multi-AZ (recommended for production) |

## Compute (ECS Fargate)

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:backendCpu` | `512` | Backend CPU units (256, 512, 1024, 2048, 4096) |
| `whispa:backendMemory` | `1024` | Backend memory in MB |
| `whispa:frontendCpu` | `256` | Frontend CPU units |
| `whispa:frontendMemory` | `512` | Frontend memory in MB |
| `whispa:desiredCount` | `1` | Number of task replicas (applies to both backend and frontend) |

## Container Images

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:backendImage` | `ghcr.io/whispa-ai/whispa-backend:latest` | Backend container image |
| `whispa:frontendImage` | `ghcr.io/whispa-ai/whispa-frontend:latest` | Frontend container image |

To upgrade versions, change the image tag:

```yaml
whispa:backendImage: ghcr.io/whispa-ai/whispa-backend:v1.2.0
whispa:frontendImage: ghcr.io/whispa-ai/whispa-frontend:v1.2.0
```

## DNS & SSL

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:hostedZoneId` | (none) | Route 53 hosted zone ID for automatic DNS and certificate provisioning |
| `whispa:autoCertificate` | `false` | Automatically request and validate ACM certificate via Route 53 (requires `hostedZoneId`) |
| `whispa:certificateArn` | (none) | ACM certificate ARN (alternative to auto-provisioning) |
| `whispa:apiDomainName` | (none) | API domain name (e.g., `api.whispa.company.com`) for host-based routing |

## Storage (S3)

S3 bucket is always created with:
- Versioning enabled
- Server-side encryption (AES-256)
- Public access blocked
- Lifecycle rules: move to IA after 30 days, Glacier after 90 days

These settings are not currently configurable via Pulumi config.

## Speech-to-Text Configuration

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:transcriptionProvider` | `elevenlabs` | Provider: `amazon`, `deepgram`, or `elevenlabs` |

**Provider details:**
- `amazon` (recommended for AWS): Uses AWS Transcribe via IAM role — no API key needed
- `deepgram`: Requires `whispa:deepgramApiKey`
- `elevenlabs`: Requires `whispa:elevenlabsApiKey`

## Email Configuration

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:mailFrom` | (required) | Verified SES sender email address |
| `whispa:mailFromName` | `Whispa` | Sender display name |
| `whispa:feedbackEmail` | Same as `mailFrom` | Feedback/support email address |

## Application URLs

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:frontendUrl` | (required) | Frontend URL with https:// (e.g., `https://whispa.company.com`) |
| `whispa:corsOrigins` | Derived from `frontendUrl` | CORS allowed origins as JSON array |
| `whispa:llmBaseUrl` | (none) | Custom LLM API base URL (defaults to OpenRouter) |

## API Keys & Integrations

| Key | Description | Required |
|-----|-------------|----------|
| `whispa:llmApiKey` | OpenRouter/LLM API key | Yes (secret) |
| `whispa:deepgramApiKey` | Deepgram STT API key | If using Deepgram (secret) |
| `whispa:elevenlabsApiKey` | ElevenLabs STT API key | If using ElevenLabs (secret) |
| `whispa:sentryDsn` | Sentry error tracking DSN | No |

## Bootstrap Admin User

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:superuserEmail` | (none) | Initial admin email (created during first migration) |
| `whispa:superuserPassword` | (none) | Initial admin password (secret) |

Set these before first deployment to auto-create an admin user.

## Feature Flags

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:showSignupCta` | `false` | Show signup CTA on landing page |

### AWS Connect Integration

Enable Amazon Connect integration for real-time call transcription from your contact center.

**Quick setup — just set your Connect instance ID:**

```bash
pulumi config set whispa:connectInstanceId "your-connect-instance-id"
pulumi up
```

This automatically:
- Adds KVS permissions (kinesisvideo:GetMedia, GetDataEndpoint, ListStreams)
- Adds Connect API permissions (connect:ListUsers, DescribeUser, etc.)
- Adds Transcribe permissions for real-time STT

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:connectInstanceId` | (none) | AWS Connect instance ID — **setting this enables all Connect permissions** |
| `whispa:kvsStreamPrefix` | `whispa-connect` | KVS stream name prefix (must match your Connect instance) |
| `whispa:enableAwsConnect` | Auto | Explicitly enable/disable (auto-enabled when connectInstanceId is set) |
| `whispa:deployConnectLambda` | Same as enableAwsConnect | Deploy the Connect Lambda via Pulumi |
| `whispa:connectApiKey` | (none) | API key for Lambda-to-backend auth |

**Finding your Connect Instance ID:**

1. Go to AWS Console → Amazon Connect → Your instance
2. Copy the Instance ID from the ARN: `arn:aws:connect:region:account:instance/<INSTANCE-ID>`

**After updating config**, restart the backend to pick up new IAM permissions:

```bash
aws ecs update-service --cluster whispa-prod --service whispa-prod-backend --force-new-deployment
```

See `infrastructure/aws-connect-lambda/README.md` for Contact Flow setup instructions.

## Environment Variables

These environment variables are automatically set from Pulumi configuration in the ECS task definitions. You should not need to set these manually.

### Backend Environment Variables

| Variable | Source | Description |
|----------|--------|-------------|
| `DB_HOST` | RDS endpoint | Database host |
| `DB_PORT` | RDS port | Database port |
| `DB_NAME` | `whispa:dbName` | Database name |
| `DB_USER` | `whispa:dbUsername` | Database username |
| `DB_PASSWORD` | Secrets Manager | Database password (auto-generated) |
| `FRONTEND_URL` | `whispa:frontendUrl` | Frontend URL for CORS |
| `CORS_ORIGINS` | `whispa:corsOrigins` | Allowed CORS origins (JSON array) |
| `LLM_API_KEY` | Secrets Manager | OpenRouter/OpenAI API key |
| `LLM_BASE_URL` | `whispa:llmBaseUrl` | Custom LLM API base URL |
| `DEEPGRAM_API_KEY` | Secrets Manager | Deepgram API key (if configured) |
| `ELEVENLABS_API_KEY` | Secrets Manager | ElevenLabs API key (if configured) |
| `S3_AUDIO_BUCKET` | S3 bucket name | Audio file storage bucket |
| `S3_AUDIO_REGION` | `aws:region` | S3 bucket region |
| `AWS_SES_REGION` | `aws:region` | SES email region |
| `MAIL_FROM` | `whispa:mailFrom` | Sender email address |
| `MAIL_FROM_NAME` | `whispa:mailFromName` | Sender display name |
| `FEEDBACK_EMAIL` | `whispa:feedbackEmail` | Feedback email address |
| `TELEPHONY_TRANSCRIPTION_PROVIDER` | `whispa:transcriptionProvider` | STT provider |
| `SHOW_SIGNUP_CTA` | `whispa:showSignupCta` | Show signup CTA flag |
| `SENTRY_DSN` | `whispa:sentryDsn` | Sentry DSN for error tracking |
| `SENTRY_ENVIRONMENT` | `whispa:environment` | Sentry environment name |
| `SUPERUSER_EMAIL` | `whispa:superuserEmail` | Bootstrap admin email |
| `SUPERUSER_PASSWORD` | Secrets Manager | Bootstrap admin password (if configured) |
| `ACCESS_SECRET_KEY` | Secrets Manager | JWT access token signing key (auto-generated) |
| `RESET_PASSWORD_SECRET_KEY` | Secrets Manager | Password reset token key (auto-generated) |
| `VERIFICATION_SECRET_KEY` | Secrets Manager | Email verification token key (auto-generated) |
| `REFRESH_SECRET_KEY` | Secrets Manager | JWT refresh token key (auto-generated) |

### Frontend Environment Variables

| Variable | Source | Description |
|----------|--------|-------------|
| `NEXT_PUBLIC_API_BASE_URL` | Derived from `whispa:apiDomainName` or `whispa:domainName` | Backend API URL |

## Example Configurations

### Minimal Production

```yaml
config:
  aws:region: us-east-1
  whispa:environment: prod
  whispa:domainName: whispa.company.com
  whispa:frontendUrl: https://whispa.company.com
  whispa:mailFrom: noreply@company.com
  whispa:hostedZoneId: Z1234567890ABC
  whispa:transcriptionProvider: amazon
  whispa:llmApiKey:
    secure: v1:xxx...
```

### High Availability Production

```yaml
config:
  aws:region: us-east-1
  whispa:environment: prod
  whispa:domainName: whispa.company.com
  whispa:frontendUrl: https://whispa.company.com
  whispa:mailFrom: noreply@company.com
  whispa:hostedZoneId: Z1234567890ABC
  whispa:transcriptionProvider: amazon

  # Multi-AZ database
  whispa:dbInstanceClass: db.t3.medium
  whispa:dbMultiAz: "true"
  whispa:dbBackupRetentionDays: "30"

  # Scaled compute
  whispa:backendCpu: "1024"
  whispa:backendMemory: "2048"
  whispa:desiredCount: "2"

  # API keys
  whispa:llmApiKey:
    secure: v1:xxx...
  whispa:sentryDsn:
    secure: v1:xxx...
```

### Development/Staging

```yaml
config:
  aws:region: us-east-1
  whispa:resourcePrefix: whispa-staging  # All resources prefixed with "whispa-staging-"
  whispa:domainName: staging.whispa.company.com
  whispa:frontendUrl: https://staging.whispa.company.com

  # Minimal resources
  whispa:dbInstanceClass: db.t3.micro
  whispa:backendCpu: "256"
  whispa:backendMemory: "512"

  whispa:llmApiKey:
    secure: v1:xxx...
  whispa:deepgramApiKey:
    secure: v1:xxx...
```

### With AWS Connect

```yaml
config:
  aws:region: ap-southeast-2
  whispa:resourcePrefix: acme-prod
  whispa:domainName: whispa.acme.com
  whispa:frontendUrl: https://whispa.acme.com
  whispa:mailFrom: noreply@acme.com

  # AWS Connect integration
  whispa:enableAwsConnect: "true"
  whispa:connectInstanceId: "a1b2c3d4-5678-90ab-cdef-EXAMPLE11111"
  whispa:kvsStreamPrefix: "acme-connect"  # Must match Connect instance stream prefix

  whispa:llmApiKey:
    secure: v1:xxx...
  whispa:elevenlabsApiKey:
    secure: v1:xxx...
```

## Setting Secrets

Use Pulumi's secret management for sensitive values:

```bash
# Set a secret
pulumi config set --secret whispa:llmApiKey "sk-or-xxx..."

# View config (secrets are encrypted)
pulumi config

# Get a specific value
pulumi config get whispa:domainName
```

## Updating Configuration

To update configuration after deployment:

```bash
# Change a value
pulumi config set whispa:desiredCount 2

# Apply the change
pulumi up
```

Some changes require resource replacement (e.g., changing VPC CIDR). Pulumi will warn you about these.
