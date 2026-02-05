# Configuration Reference

This document describes all configuration options for your Whispa deployment.

## Pulumi Configuration

Configuration is set in your `Pulumi.<stack>.yaml` file.

### Required Configuration

| Key | Description | Example |
|-----|-------------|---------|
| `aws:region` | AWS region for deployment | `us-east-1` |
| `whispa:domainName` | Your domain for Whispa | `whispa.company.com` |
| `whispa:frontendUrl` | Full frontend URL | `https://whispa.company.com` |
| `whispa:mailFrom` | Verified SES sender email | `noreply@company.com` |
| `whispa:llmApiKey` | OpenRouter/LLM API key | (secret) |

### Resource Naming

Control how AWS resources are named:

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:resourcePrefix` | (none) | Custom prefix for all resources (e.g., `whispa-dev`, `acme-prod`) |
| `whispa:projectName` | `whispa` | Project name (used if resourcePrefix not set) |
| `whispa:environment` | Stack name | Environment name (used if resourcePrefix not set) |

**Resource naming behavior:**
- If `resourcePrefix` is set: `{resourcePrefix}-{resourceType}` (e.g., `whispa-dev-ecs-task`)
- If not set: `{projectName}-{environment}-{resourceType}` (e.g., `whispa-prod-ecs-task`)

### Networking

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:vpcCidr` | `10.0.0.0/16` | VPC CIDR block |
| `whispa:availabilityZones` | 2 | Number of AZs to use |
| `whispa:createNatGateway` | `true` | Create NAT gateway for private subnets |

### Database (RDS)

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:dbInstanceClass` | `db.t3.medium` | RDS instance type |
| `whispa:dbAllocatedStorage` | `20` | Storage in GB |
| `whispa:dbBackupRetentionDays` | `7` | Backup retention in days |
| `whispa:dbMultiAz` | `false` | Enable Multi-AZ (production recommended) |

### Compute (ECS Fargate)

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:backendCpu` | `512` | Backend CPU units (256, 512, 1024, 2048, 4096) |
| `whispa:backendMemory` | `1024` | Backend memory in MB |
| `whispa:frontendCpu` | `256` | Frontend CPU units |
| `whispa:frontendMemory` | `512` | Frontend memory in MB |
| `whispa:desiredCount` | `1` | Number of task replicas (applies to both backend and frontend) |

### Container Images

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:backendImage` | `ghcr.io/whispa-ai/whispa-backend:latest` | Backend container image |
| `whispa:frontendImage` | `ghcr.io/whispa-ai/whispa-frontend:latest` | Frontend container image |

### DNS & SSL

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:hostedZoneId` | (none) | Route 53 hosted zone ID for automatic DNS and certificate provisioning |
| `whispa:autoCertificate` | `false` | Automatically request and validate ACM certificate via Route 53 (requires `hostedZoneId`) |
| `whispa:certificateArn` | (none) | ACM certificate ARN (alternative to auto-provisioning) |
| `whispa:apiDomainName` | (none) | API domain name (e.g., `api.whispa.company.com`) |

### Storage (S3)

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:enableS3Versioning` | `true` | Enable S3 versioning |
| `whispa:s3LifecycleTransitionDays` | `30` | Days before moving to IA storage |
| `whispa:s3LifecycleGlacierDays` | `90` | Days before moving to Glacier |

### Speech-to-Text Configuration

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:transcriptionProvider` | `elevenlabs` | Provider: `amazon`, `deepgram`, or `elevenlabs` |

**Provider details:**
- `amazon` (recommended for AWS): Uses AWS Transcribe via IAM role — no API key needed
- `deepgram`: Requires `whispa:deepgramApiKey`
- `elevenlabs`: Requires `whispa:elevenlabsApiKey`

### API Keys & Integrations

| Key | Description | Required |
|-----|-------------|----------|
| `whispa:llmApiKey` | OpenRouter/LLM API key | Yes |
| `whispa:deepgramApiKey` | Deepgram STT API key | If using Deepgram |
| `whispa:elevenlabsApiKey` | ElevenLabs STT API key | If using ElevenLabs |
| `whispa:sentryDsn` | Sentry error tracking DSN | No |
| `whispa:langfusePublicKey` | Langfuse public key | No |
| `whispa:langfuseSecretKey` | Langfuse secret key | No |

### Feature Flags

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:showSignupCta` | `false` | Show signup CTA on landing page |
| `whispa:piiScrubEnabled` | `true` | Enable PII scrubbing |
| `whispa:piiScrubBeforeDatabase` | `true` | Scrub PII before database storage |

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
| `NEXT_PUBLIC_API_BASE_URL` | Backend API URL |
| `NEXT_PUBLIC_SENTRY_DSN` | Frontend Sentry DSN |

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
