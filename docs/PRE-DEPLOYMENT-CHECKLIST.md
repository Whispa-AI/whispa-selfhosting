# Pre-Deployment Checklist

Complete these items **before** your scheduled deployment session. This ensures a smooth deployment process.

## Required Items

### 1. AWS Account

| Item | Details | How to Verify |
|------|---------|---------------|
| AWS Account | Active account with billing enabled | Login to AWS Console |
| Admin Access | IAM user/role with AdministratorAccess | `aws sts get-caller-identity` returns your account |
| Region Selected | Choose deployment region (e.g., `ap-southeast-2`) | Note your preferred region |

### 2. Domain & DNS

| Item | Details | Status |
|------|---------|--------|
| Domain Name | The domain where Whispa will be hosted (e.g., `whispa.yourcompany.com`) | ☐ |
| DNS Access | Ability to add DNS records at your DNS provider | ☐ |
| Subdomain Decision | Whether to use a subdomain (recommended) or root domain | ☐ |

**If using Route53 (recommended):**
- We'll create a hosted zone and you'll add NS records at your DNS provider
- This enables automatic SSL certificate validation

**If NOT using Route53:**
- You'll need to manually add DNS validation records for SSL certificates
- Certificate validation can take up to 30 minutes

### 3. Email (for notifications)

| Item | Details | Status |
|------|---------|--------|
| Sender Email | Email address for system notifications (e.g., `noreply@yourcompany.com`) | ☐ |
| SES Verification | Verify this email in AWS SES before deployment | ☐ |

**To verify email in SES:**
1. AWS Console → SES → Verified Identities → Create Identity
2. Choose "Email address" and enter your sender email
3. Click the verification link sent to that email

### 4. LLM API Key

| Provider | What You Need |
|----------|---------------|
| OpenRouter (recommended) | API key from [openrouter.ai](https://openrouter.ai) |
| OpenAI | API key from [platform.openai.com](https://platform.openai.com) |
| Azure OpenAI | Endpoint URL, API key, deployment name |

You need at least one LLM provider configured. OpenRouter is recommended as it provides access to multiple models.

## Optional Items

### AWS Connect Integration

*Only if you're using Amazon Connect for call center integration:*

| Item | Details | Status |
|------|---------|--------|
| Connect Instance | Active AWS Connect instance | ☐ |
| Instance ID | Found in Connect ARN: `arn:aws:connect:region:account:instance/<ID>` | ☐ |
| KVS Stream Prefix | Your Connect instance's Kinesis Video Stream prefix | ☐ |

### Error Tracking

| Item | Details |
|------|---------|
| Sentry DSN | From [sentry.io](https://sentry.io) (free tier available) |

### Speech-to-Text

**Default: AWS Transcribe** (recommended - no additional setup needed)

*Alternative providers (if preferred):*
| Provider | What You Need |
|----------|---------------|
| Deepgram | API key from [deepgram.com](https://deepgram.com) |
| ElevenLabs | API key from [elevenlabs.io](https://elevenlabs.io) |

## Information to Have Ready

Please have this information available during the deployment call:

```
Domain name:          ___________________________
AWS region:           ___________________________
Sender email:         ___________________________
LLM provider:         ___________________________
LLM API key:          ___________________________

Admin user email:     ___________________________
Admin user password:  ___________________________

(Optional)
Sentry DSN:           ___________________________
Connect Instance ID:  ___________________________
```

## Estimated Costs

Monthly AWS costs for a small deployment (~10 users):

| Service | Estimated Cost |
|---------|---------------|
| RDS (db.t3.small) | ~$25/month |
| ECS Fargate | ~$30/month |
| NAT Gateway | ~$35/month |
| ALB | ~$20/month |
| S3 + Data Transfer | ~$5/month |
| **Total** | **~$115/month** |

*Costs vary by region and usage. First deployment may include one-time certificate costs.*

## Deployment Timeline

| Phase | Duration |
|-------|----------|
| Configuration | 15-20 min |
| Infrastructure deployment | 15-20 min |
| DNS propagation | 5-30 min |
| Verification & testing | 10-15 min |
| **Total** | **45-90 min** |

## Questions?

Contact support@whispa.ai before your deployment session if you have questions about any items on this checklist.
