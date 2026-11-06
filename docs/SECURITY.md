# Security Best Practices

This guide covers security considerations for your Whispa deployment.

## Infrastructure Security

### Network Isolation

The default deployment creates:
- **Public subnets**: Only for ALB (internet-facing)
- **Private subnets**: ECS tasks and RDS (no direct internet access)
- **NAT Gateway**: Controlled outbound internet access

```
Internet → ALB (public) → ECS Tasks (private) → RDS (private)
                                    ↓
                              NAT Gateway → External APIs
```

### Security Groups

| Resource | Inbound | Outbound |
|----------|---------|----------|
| ALB | 443 from 0.0.0.0/0 | ECS security group |
| ECS Tasks | ALB security group | 0.0.0.0/0 (via NAT) |
| RDS | ECS security group (5432) | None |

### Encryption

**In Transit**:
- All external traffic uses TLS 1.2+
- ALB terminates SSL with ACM certificates
- Internal traffic uses HTTPS between services

**At Rest**:
- RDS encryption enabled by default
- S3 bucket encryption (AES-256 or KMS)
- Secrets encrypted with KMS in Secrets Manager

## Credential Management

### Secrets Manager

All sensitive credentials are stored in AWS Secrets Manager:
- Database credentials
- API keys (OpenRouter, Deepgram, etc.)
- JWT signing keys

**Never commit secrets to git or configuration files.**

### Setting Secrets with Pulumi

```bash
# Set secrets (encrypted in Pulumi state)
pulumi config set --secret whispa:llmApiKey "sk-or-..."
pulumi config set --secret whispa:deepgramApiKey "..."

# View config (secrets are masked)
pulumi config
```

### Rotating Secrets

1. Update the secret in Secrets Manager
2. Redeploy ECS tasks to pick up new values:
   ```bash
   aws ecs update-service --cluster whispa-prod --service whispa-prod-backend --force-new-deployment
   ```

## Access Control

### IAM Roles

The deployment creates minimal IAM roles:

**ECS Task Role**:
- S3 bucket access (read/write audio files)
- Secrets Manager access (read secrets)
- CloudWatch Logs (write logs)

**ECS Execution Role**:
- ECR/GHCR image pull
- Secrets Manager (for initial secret fetch)
- CloudWatch Logs

### Database Access

RDS is only accessible from:
- ECS tasks via security group
- AWS SSM Session Manager (if bastion configured)

**No direct internet access to database.**

## Application Security

### Authentication

Whispa uses:
- **JWT tokens** for API authentication
- **Secure cookies** with `httpOnly`, `secure`, `sameSite` flags
- **CSRF protection** via double-submit cookie pattern

### Authorization

- Role-based access control (admin, user)
- Users can only access their own data
- Admin users can access all data

### PII Handling

Whispa includes PII scrubbing:

```yaml
# In Pulumi config
whispa:piiScrubEnabled: "true"
whispa:piiScrubBeforeDatabase: "true"
```

This redacts:
- Phone numbers
- Email addresses
- Credit card numbers
- SSNs and other identifiers

**Note**: Person names are excluded by default to preserve conversation context.

## Compliance Considerations

### Data Residency

Deploy in a region that meets your compliance requirements:
- EU data: `eu-west-1`, `eu-central-1`
- US data: `us-east-1`, `us-west-2`
- Australia: `ap-southeast-2`

### Audit Logging

Enable CloudTrail for AWS API audit logs:

```bash
aws cloudtrail create-trail \
  --name whispa-audit \
  --s3-bucket-name your-audit-bucket
```

### Backup & Recovery

- RDS automated backups (default: 7 days retention)
- S3 versioning enabled for audio files
- Consider cross-region replication for DR

## Security Hardening Checklist

### Pre-Deployment

- [ ] Use dedicated AWS account for Whispa
- [ ] Enable MFA for AWS root account
- [ ] Create IAM user with minimal permissions
- [ ] Use Pulumi secrets for all sensitive values

### Post-Deployment

- [ ] Verify no public access to RDS
- [ ] Confirm S3 bucket blocks public access
- [ ] Test that ECS tasks only accept ALB traffic
- [ ] Enable AWS GuardDuty for threat detection
- [ ] Set up CloudWatch alarms for anomalies

### Ongoing

- [ ] Regularly update to latest Whispa version
- [ ] Rotate API keys periodically
- [ ] Review CloudWatch logs for anomalies
- [ ] Audit IAM roles and permissions
- [ ] Test backup/restore procedures

## Vulnerability Reporting

If you discover a security vulnerability in Whispa:

1. **Do not** open a public GitHub issue
2. Email security@whispa.net with details
3. We'll respond within 48 hours
4. We follow responsible disclosure practices

## Security Updates

Subscribe to security updates:
- Watch the [GitHub repository](https://github.com/Whispa-AI/whispa-selfhosting)
- Check release notes for security fixes
- Apply patches promptly

## Third-Party Services

Whispa sends data to an external service only when your configuration selects it. Everything else, including the database, call storage and the self-hosted AssemblyAI speech server, stays in your AWS account.

| Service | Used when | Data sent |
|---------|-----------|-----------|
| Amazon Bedrock | Default for post-call analysis and the voice agent, in your AWS account and region | Transcripts and call context. A `global.` or other cross-region model ID may be served outside your region |
| SCX.ai | `AI_CALL_LLM_MODEL` is an `scx/…` model | Voice-agent conversation. Hosting location depends on the model; confirm it with SCX.ai |
| Google Cloud Vertex AI | A `vertex/…` model is selected, as the voice agent's model or its backup | Voice-agent conversation, processed in the endpoint's region |
| OpenRouter | An `openrouter/…` model is selected | Conversation or transcripts, forwarded to the hosts in `OPENROUTER_PROVIDER_ORDER` |
| Deepgram | `AI_CALL_STT_PROVIDER=deepgram` | Caller audio on voice-agent calls |
| AssemblyAI cloud | Transcription points at AssemblyAI's cloud API instead of a self-hosted server | Call audio |
| Cartesia | Voice agent speech (`CARTESIA_API_KEY`) | The text the agent speaks |
| ElevenLabs | Training calls, or ElevenLabs batch transcription | Training-call audio, or call audio |
| Twilio | SMS or Twilio Voice is configured | Phone numbers, message text, call audio |
| Sentry | `SENTRY_DSN` is set | Error events. Names, phone numbers and call content are scrubbed before sending |

Review each provider's data-retention and hosting terms for your compliance needs, and record the ones you enable as subprocessors.
