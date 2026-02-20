# Configuration Reference

This document describes all configuration options for your Whispa deployment.

## How Configuration Works

Pulumi configuration lives in a `Pulumi.<stack>.yaml` file. **You can edit this file directly** instead of running `pulumi config set` for each value. The only exception is **secrets** (API keys, passwords), which must be set via the CLI so Pulumi can encrypt them:

```bash
pulumi config set --secret whispa:superuserPassword "your-password"
```

To get started quickly, copy the example file:

```bash
cd infra/pulumi
cp Pulumi.dev.yaml.example Pulumi.<your-stack>.yaml
# Edit the file with your values, then set secrets:
pulumi config set --secret whispa:superuserPassword "your-password"
```

## Required Configuration

| Key | Description | Example |
|-----|-------------|---------|
| `aws:region` | AWS region for deployment | `us-east-1` |
| `whispa:domainName` | Your domain for Whispa | `whispa.company.com` |
| `whispa:frontendUrl` | Full frontend URL | `https://whispa.company.com` |
| `whispa:mailFrom` | Verified SES sender email | `noreply@company.com` |

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

## LLM Model Configuration

Configure which LLM model each analyzer uses. Models are specified with a provider prefix (e.g., `bedrock/`, `openrouter/`).

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:llmModelDefault` | (none) | Default model used as fallback for all analyzers |
| `whispa:llmModelActionCards` | (uses default) | Model for action cards analyzer |
| `whispa:llmModelWorkflow` | (uses default) | Model for workflow progress analyzer |
| `whispa:llmModelSuggestedResponses` | (uses default) | Model for suggested responses analyzer |
| `whispa:llmModelSentiment` | (uses default) | Model for sentiment analyzer |
| `whispa:llmModelCoaching` | (uses default) | Model for post-call coaching feedback |
| `whispa:llmModelSummary` | (uses default) | Model for post-call summary generation |
| `whispa:llmModelClassification` | (uses default) | Model for call classification |

**Provider prefixes:**
- `bedrock/` — AWS Bedrock (uses IAM role, requires `bedrockRegion`). Example: `bedrock/anthropic.claude-3-5-sonnet-20241022-v2:0`
- `openrouter/` — OpenRouter (requires `llmApiKey`). Example: `openrouter/google/gemini-2.5-flash`

## AWS Bedrock Configuration

Use AWS Bedrock as your LLM provider to keep all AI traffic within your AWS account.

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:bedrockRegion` | (none) | AWS region for Bedrock API calls. Setting this enables Bedrock IAM permissions on the ECS task role |

**Note:** When `bedrockRegion` is set, the deployment automatically grants `bedrock:InvokeModel` and `bedrock:InvokeModelWithResponseStream` permissions to the ECS task role. No API key is needed — authentication uses the task's IAM role.

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
| `whispa:llmApiKey` | OpenRouter/LLM API key | Only if using OpenRouter (not needed for Bedrock) |
| `whispa:deepgramApiKey` | Deepgram STT API key | If using Deepgram (secret) |
| `whispa:elevenlabsApiKey` | ElevenLabs STT API key | If using ElevenLabs (secret) |
| `whispa:sentryDsn` | Sentry error tracking DSN | No |

## Observability

### Langfuse (LLM Observability)

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:langfusePublicKey` | (none) | Langfuse public key |
| `whispa:langfuseSecretKey` | (none) | Langfuse secret key (secret) |
| `whispa:langfuseHost` | (none) | Langfuse host URL (for self-hosted Langfuse, defaults to cloud) |

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
- Generates an API key for Lambda-to-backend authentication

| Key | Default | Description |
|-----|---------|-------------|
| `whispa:connectInstanceId` | (none) | AWS Connect instance ID — **setting this enables all Connect permissions** |
| `whispa:kvsStreamPrefix` | `whispa-connect` | KVS stream name prefix (must match your Connect instance) |
| `whispa:enableAwsConnect` | Auto | Explicitly enable/disable (auto-enabled when connectInstanceId is set) |
| `whispa:deployConnectLambda` | Same as enableAwsConnect | Deploy the Contact Flow Lambda via Pulumi |
| `whispa:deployEventBridgeConsumer` | Same as enableAwsConnect | Deploy the EventBridge consumer Lambda + rule via Pulumi |

**Note:** The Connect API key for Lambda-to-backend authentication is auto-generated and stored in AWS Secrets Manager. No manual configuration needed.

#### EventBridge Integration (Hybrid Model)

Whispa uses a hybrid architecture for Connect integration:

1. **Contact Flow Lambda** — invoked synchronously from a Contact Flow to start KVS audio capture (`/awsconnect/call-started`)
2. **EventBridge Consumer Lambda** — receives asynchronous contact events via EventBridge for participant state tracking (`/awsconnect/eventbridge`)

When `deployEventBridgeConsumer` is enabled (default when `connectInstanceId` is set), Pulumi creates:
- A Node.js Lambda that forwards Connect events to the Whispa backend
- An EventBridge rule filtered to `Amazon Connect Contact Event` from your specific Connect instance
- IAM permissions and retry policy (2 retries, 5-minute max age)

To disable the EventBridge consumer (e.g., if you manage EventBridge rules externally):

```bash
pulumi config set whispa:deployEventBridgeConsumer false
```

**Finding your Connect Instance ID:**

1. Go to AWS Console > Amazon Connect > Your instance
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
| `LLM_API_KEY` | Secrets Manager | OpenRouter/OpenAI API key (if configured) |
| `LLM_BASE_URL` | `whispa:llmBaseUrl` | Custom LLM API base URL |
| `LLM_MODEL_DEFAULT` | `whispa:llmModelDefault` | Default LLM model identifier |
| `LLM_MODEL_ACTION_CARDS` | `whispa:llmModelActionCards` | Model for action cards analyzer |
| `LLM_MODEL_WORKFLOW` | `whispa:llmModelWorkflow` | Model for workflow progress analyzer |
| `LLM_MODEL_SUGGESTED_RESPONSES` | `whispa:llmModelSuggestedResponses` | Model for suggested responses analyzer |
| `LLM_MODEL_SENTIMENT` | `whispa:llmModelSentiment` | Model for sentiment analyzer |
| `LLM_MODEL_COACHING` | `whispa:llmModelCoaching` | Model for coaching feedback |
| `LLM_MODEL_SUMMARY` | `whispa:llmModelSummary` | Model for summary generation |
| `LLM_MODEL_CLASSIFICATION` | `whispa:llmModelClassification` | Model for call classification |
| `AWS_BEDROCK_REGION` | `whispa:bedrockRegion` | AWS region for Bedrock API calls |
| `AWS_TRANSCRIBE_REGION` | `aws:region` | AWS Transcribe region |
| `AWS_CONNECT_REGION` | `aws:region` | AWS Connect region |
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
| `LANGFUSE_PUBLIC_KEY` | `whispa:langfusePublicKey` | Langfuse public key |
| `LANGFUSE_SECRET_KEY` | Secrets Manager | Langfuse secret key (if configured) |
| `LANGFUSE_HOST` | `whispa:langfuseHost` | Langfuse host URL |
| `CONNECT_API_KEY` | Secrets Manager | Lambda-to-backend auth key (auto-generated) |
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

### Minimal Production (Bedrock)

```yaml
config:
  aws:region: ap-southeast-2
  whispa:domainName: whispa.company.com
  whispa:frontendUrl: https://whispa.company.com
  whispa:mailFrom: noreply@company.com
  whispa:hostedZoneId: Z1234567890ABC
  whispa:autoCertificate: true
  whispa:transcriptionProvider: amazon
  whispa:bedrockRegion: ap-southeast-2
  whispa:llmModelDefault: bedrock/anthropic.claude-3-5-sonnet-20241022-v2:0
  whispa:superuserEmail: admin@company.com
  whispa:superuserPassword:
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
  whispa:bedrockRegion: us-east-1
  whispa:llmModelDefault: bedrock/anthropic.claude-3-5-sonnet-20241022-v2:0

  # Multi-AZ database
  whispa:dbInstanceClass: db.t3.medium
  whispa:dbMultiAz: "true"
  whispa:dbBackupRetentionDays: "30"

  # Scaled compute
  whispa:backendCpu: "1024"
  whispa:backendMemory: "2048"
  whispa:desiredCount: "2"

  # Observability
  whispa:sentryDsn: https://xxx@sentry.io/xxx
```

### With AWS Connect

```yaml
config:
  aws:region: ap-southeast-2
  whispa:resourcePrefix: acme-prod
  whispa:domainName: whispa.acme.com
  whispa:frontendUrl: https://whispa.acme.com
  whispa:mailFrom: noreply@acme.com
  whispa:bedrockRegion: ap-southeast-2
  whispa:llmModelDefault: bedrock/anthropic.claude-3-5-sonnet-20241022-v2:0

  # AWS Connect integration
  whispa:connectInstanceId: "a1b2c3d4-5678-90ab-cdef-EXAMPLE11111"
  whispa:kvsStreamPrefix: "acme-connect"
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
