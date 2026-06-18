# Changelog

All notable changes to the Whispa Self-Hosting repository will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

> **Versioning:** from the next release onward, this repo tracks the **product
> version** (the `0.0.x` line shared with the `whispa-backend`/`whispa-frontend`
> images), so an infra tag `v0.0.N` deploys app images `0.0.N`. This supersedes the
> standalone `1.0.0` baseline below.

## [Unreleased]

## [0.0.77] - 2026-06-18

## [0.0.76] - 2026-06-16

## [0.0.75] - 2026-06-15

## [0.0.74] - 2026-06-15

## [0.0.73] - 2026-06-15

## [0.0.72] - 2026-06-15

### Added
- `whispa:imageTag` / `whispa:imageRegistry` — pin the app version with a single
  key; checking out infra at a release tag deploys the matching images by default.
- AWS Bedrock LLM support and per-analyzer model configuration (`whispa:bedrockRegion`,
  `whispa:llmModelDefault`, and per-analyzer overrides).
- AWS Connect integration: Contact Flow Lambda + custom resource prefix support.
- EventBridge consumer Lambda for Connect contact events.
- AssemblyAI streaming transcription provider option.
- QA scorecard model configuration (`whispa:llmModelScorecard`).
- `whispa:seedScenarios` — auto-seed catalog scenarios on container startup.
- RDS I/O CloudWatch alarms (configurable thresholds + SNS/email notification).
- Superuser password stored as a dedicated secret with IAM read access.

### Changed
- Superuser email and password are now **mandatory** (bootstrap admin is created on
  first deploy).
- IAM permissions allow Bedrock **cross-region inference profiles**.
- Documentation: config reference synced with code, client pre-deployment checklist,
  SSL auto-provisioning and external-DNS/STT clarifications.

## [1.0.0] - 2026-01-29

### Added
- Initial release of Whispa self-hosting infrastructure
- Pulumi IaC for AWS deployment
- ECS Fargate for backend and frontend services
- RDS PostgreSQL database
- S3 bucket for audio storage with lifecycle policies
- Application Load Balancer with SSL/TLS
- Route 53 DNS integration (optional)
- AWS Secrets Manager for credential storage
- Comprehensive documentation
  - Prerequisites guide
  - Deployment guide
  - Configuration reference
  - Upgrade guide
  - Troubleshooting guide
  - Security best practices

### Infrastructure Components
- VPC with public/private subnets
- NAT Gateway for outbound internet access
- Security groups with least-privilege access
- IAM roles for ECS tasks
- CloudWatch log groups
- ACM certificates for SSL/TLS

### Supported Integrations
- OpenRouter / OpenAI for LLM
- Deepgram for speech-to-text
- ElevenLabs for speech-to-text
- Sentry for error tracking (optional)
- Langfuse for LLM observability (optional)

[Unreleased]: https://github.com/Whispa-AI/whispa-selfhosting/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/Whispa-AI/whispa-selfhosting/releases/tag/v1.0.0
