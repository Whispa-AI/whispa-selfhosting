# Changelog

All notable changes to the Whispa Self-Hosting repository will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
