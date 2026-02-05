# Pre-Deployment Checklist

Complete these items **before** your scheduled deployment session. This ensures a smooth deployment process.

## Required Items

### 1. AWS Account

| Item | Details | How to Verify |
|------|---------|---------------|
| AWS Account | Active account with billing enabled | Login to AWS Console |
| Admin Access | IAM user/role with AdministratorAccess | `aws sts get-caller-identity` returns your account |
| Region Selected | Choose deployment region (e.g., `ap-southeast-2`) | Note your preferred region |

### 2. Domain & DNS Setup

| Item | Status |
|------|--------|
| Subdomain decided (e.g., `clientname.whispa.net`) | ☐ |
| Route53 hosted zone created | ☐ |
| NS records added at your DNS provider | ☐ |
| NS records propagated (verify with `dig NS clientname.whispa.net`) | ☐ |

**Setup steps:**

1. **Create Route53 hosted zone** (AWS Console → Route53 → Hosted zones → Create):
   - Enter your chosen subdomain (e.g., `clientname.whispa.net`)
   - Note the **Hosted Zone ID** (e.g., `Z0123456789ABC`)

2. **Copy the 4 NS records** shown in the hosted zone (e.g., `ns-123.awsdns-12.com`)

3. **Add NS records at your DNS provider** (GoDaddy, Cloudflare, etc.):
   | Type | Name | Value |
   |------|------|-------|
   | NS | clientname | ns-123.awsdns-12.com |
   | NS | clientname | ns-456.awsdns-34.net |
   | NS | clientname | ns-789.awsdns-56.org |
   | NS | clientname | ns-012.awsdns-78.co.uk |

4. **Verify propagation** (can take 5-60 minutes):
   ```bash
   dig NS clientname.whispa.net
   ```
   You should see the AWS nameservers in the response.

This delegation enables:
- **Automatic SSL certificate** creation and validation (no manual certificate setup needed)
- **Automatic DNS records** pointing to the load balancer

### 3. Email (for notifications)

| Item | Status |
|------|--------|
| Sender email decided (e.g., `noreply@clientname.whispa.net`) | ☐ |
| Email verified in AWS SES | ☐ |

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

Choose one provider:

| Provider | What You Need |
|----------|---------------|
| AWS Transcribe (recommended) | No API key needed — uses IAM |
| Deepgram | API key from [deepgram.com](https://deepgram.com) |
| ElevenLabs | API key from [elevenlabs.io](https://elevenlabs.io) |

## Information to Have Ready

Please have this information available during the deployment call:

```
Domain name:            ___________________________
Route53 Hosted Zone ID: ___________________________
AWS region:             ___________________________
Sender email:           ___________________________

LLM provider:           ___________________________
LLM API key:            ___________________________

STT provider:           ___________________________
STT API key (if not AWS): ___________________________

Admin user email:       ___________________________
Admin user password:    ___________________________

(Optional)
Sentry DSN:             ___________________________
Connect Instance ID:    ___________________________
```

## Questions?

Contact support@whispa.ai before your deployment session if you have questions about any items on this checklist.
