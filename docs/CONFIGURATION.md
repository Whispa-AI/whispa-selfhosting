# Configuration Reference

This document describes all configuration options for your Whispa deployment.

## Pulumi Configuration

Configuration is set in your `Pulumi.<stack>.yaml` file.

### Required Configuration

| Key | Description | Example |
|-----|-------------|---------|
| `aws:region` | AWS region for deployment | `us-east-1` |
| `whispa:environment` | Environment name (used in resource naming) | `prod` |
| `whispa:domain` | Your domain for Whispa | `whispa.company.com` |
| `whispa:openRouterApiKey` | OpenRouter API key for LLM | (secret) |
| `whispa:deepgramApiKey` | Deepgram API key for STT | (secret) |

### Networking

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:vpcCidr` | `10.0.0.0/16` | VPC CIDR block |
| `whispa:availabilityZones` | 2 | Number of AZs to use |
| `whispa:createNatGateway` | `true` | Create NAT gateway for private subnets |

### Database (RDS)

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:dbInstanceClass` | `db.t3.small` | RDS instance type |
| `whispa:dbAllocatedStorage` | `20` | Storage in GB |
| `whispa:dbMaxAllocatedStorage` | `100` | Max storage for autoscaling |
| `whispa:dbBackupRetentionPeriod` | `7` | Backup retention in days |
| `whispa:dbMultiAz` | `false` | Enable Multi-AZ (production recommended) |

### Compute (ECS Fargate)

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:backendCpu` | `512` | Backend CPU units (256, 512, 1024, 2048, 4096) |
| `whispa:backendMemory` | `1024` | Backend memory in MB |
| `whispa:backendDesiredCount` | `1` | Number of backend tasks |
| `whispa:frontendCpu` | `256` | Frontend CPU units |
| `whispa:frontendMemory` | `512` | Frontend memory in MB |
| `whispa:frontendDesiredCount` | `1` | Number of frontend tasks |

### Container Images

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:backendImage` | `ghcr.io/whispa-ai/whispa-backend:latest` | Backend container image |
| `whispa:frontendImage` | `ghcr.io/whispa-ai/whispa-frontend:latest` | Frontend container image |
| `whispa:imageTag` | `latest` | Override image tag for both |

### DNS & SSL

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:route53ZoneId` | (none) | Route 53 hosted zone ID for automatic DNS |
| `whispa:createWildcardCert` | `true` | Create wildcard SSL certificate |
| `whispa:apiSubdomain` | `api` | Subdomain for backend API |

### Storage (S3)

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:enableS3Versioning` | `true` | Enable S3 versioning |
| `whispa:s3LifecycleTransitionDays` | `30` | Days before moving to IA storage |
| `whispa:s3LifecycleGlacierDays` | `90` | Days before moving to Glacier |

### API Keys & Integrations

| Key | Description | Required |
|-----|-------------|----------|
| `whispa:openRouterApiKey` | OpenRouter API key | Yes |
| `whispa:deepgramApiKey` | Deepgram STT API key | If using Deepgram |
| `whispa:elevenlabsApiKey` | ElevenLabs STT API key | If using ElevenLabs |
| `whispa:sentryDsn` | Sentry error tracking DSN | No |
| `whispa:langfusePublicKey` | Langfuse public key | No |
| `whispa:langfuseSecretKey` | Langfuse secret key | No |

### Feature Flags

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:showSignupCta` | `true` | Show signup CTA on landing page |
| `whispa:piiScrubEnabled` | `true` | Enable PII scrubbing |
| `whispa:piiScrubBeforeDatabase` | `true` | Scrub PII before database storage |

## Environment Variables

These environment variables are automatically set from Pulumi configuration. You can also override them in the ECS task definition.

### Backend Environment Variables

| Variable | Description |
|----------|-------------|
| `DATABASE_URL` | PostgreSQL connection string (from Secrets Manager) |
| `FRONTEND_URL` | Frontend URL for CORS |
| `CORS_ORIGINS` | Allowed CORS origins |
| `LLM_API_KEY` | OpenRouter/OpenAI API key |
| `LLM_BASE_URL` | LLM API base URL |
| `DEEPGRAM_API_KEY` | Deepgram API key |
| `ELEVENLABS_API_KEY` | ElevenLabs API key |
| `S3_AUDIO_BUCKET` | S3 bucket for audio files |
| `S3_AUDIO_REGION` | S3 bucket region |
| `SENTRY_DSN` | Sentry DSN for error tracking |
| `SENTRY_ENVIRONMENT` | Sentry environment name |

### Frontend Environment Variables

| Variable | Description |
|----------|-------------|
| `NEXT_PUBLIC_API_URL` | Backend API URL |
| `NEXT_PUBLIC_SENTRY_DSN` | Frontend Sentry DSN |

## Example Configurations

### Minimal Production

```yaml
config:
  aws:region: us-east-1
  whispa:environment: prod
  whispa:domain: whispa.company.com
  whispa:route53ZoneId: Z1234567890ABC
  whispa:openRouterApiKey:
    secure: v1:xxx...
  whispa:deepgramApiKey:
    secure: v1:xxx...
```

### High Availability Production

```yaml
config:
  aws:region: us-east-1
  whispa:environment: prod
  whispa:domain: whispa.company.com
  whispa:route53ZoneId: Z1234567890ABC

  # Multi-AZ database
  whispa:dbInstanceClass: db.t3.medium
  whispa:dbMultiAz: "true"
  whispa:dbBackupRetentionPeriod: "30"

  # Scaled compute
  whispa:backendCpu: "1024"
  whispa:backendMemory: "2048"
  whispa:backendDesiredCount: "2"
  whispa:frontendDesiredCount: "2"

  # API keys
  whispa:openRouterApiKey:
    secure: v1:xxx...
  whispa:deepgramApiKey:
    secure: v1:xxx...
  whispa:sentryDsn:
    secure: v1:xxx...
```

### Development/Staging

```yaml
config:
  aws:region: us-east-1
  whispa:environment: staging
  whispa:domain: staging.whispa.company.com

  # Minimal resources
  whispa:dbInstanceClass: db.t3.micro
  whispa:backendCpu: "256"
  whispa:backendMemory: "512"

  # Disable some features for cost
  whispa:createNatGateway: "false"  # Use VPC endpoints instead

  whispa:openRouterApiKey:
    secure: v1:xxx...
  whispa:deepgramApiKey:
    secure: v1:xxx...
```

## Setting Secrets

Use Pulumi's secret management for sensitive values:

```bash
# Set a secret
pulumi config set --secret whispa:openRouterApiKey "sk-or-xxx..."

# View config (secrets are encrypted)
pulumi config

# Get a specific value
pulumi config get whispa:domain
```

## Updating Configuration

To update configuration after deployment:

```bash
# Change a value
pulumi config set whispa:backendDesiredCount 2

# Apply the change
pulumi up
```

Some changes require resource replacement (e.g., changing VPC CIDR). Pulumi will warn you about these.
