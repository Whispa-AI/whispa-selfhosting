# Changelog

All notable changes to the Whispa Self-Hosting repository will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

> **Versioning:** from the next release onward, this repo tracks the **product
> version** (the `0.0.x` line shared with the `whispa-backend`/`whispa-frontend`
> images), so an infra tag `v0.0.N` deploys app images `0.0.N`. This supersedes the
> standalone `1.0.0` baseline below.

## [Unreleased]

## [0.0.117] - 2026-07-14

## [0.0.116] - 2026-07-14

## [0.0.115] - 2026-07-14

## [0.0.114] - 2026-07-13

## [0.0.113] - 2026-07-13

## [0.0.112] - 2026-07-13

## [0.0.111] - 2026-07-13

## [0.0.110] - 2026-07-12

## [0.0.109] - 2026-07-12

## [0.0.108] - 2026-07-12

## [0.0.107] - 2026-07-09

## [0.0.106] - 2026-07-08

## [0.0.105] - 2026-07-08

## [0.0.104] - 2026-07-07

## [0.0.103] - 2026-07-06

## [0.0.102] - 2026-07-06

## [0.0.101] - 2026-07-05

## [0.0.100] - 2026-07-02

## [0.0.99] - 2026-07-02

## [0.0.98] - 2026-07-01

## [0.0.97] - 2026-07-01

## [0.0.96] - 2026-07-01

## [0.0.95] - 2026-07-01

## [0.0.94] - 2026-06-30

## [0.0.93] - 2026-06-30

## [0.0.92] - 2026-06-25

## [0.0.91] - 2026-06-25

## [0.0.90] - 2026-06-24

### Added
- Example **manual deploy pipeline** (`.github/workflows/deploy.yml`): pick an
  environment (`dev`/`test`), enter a version, press Run — stages the stack config
  and runs `pulumi up` via AWS OIDC. Stack configs live in `stacks/`. Ships
  commented-out (no trigger) so it doesn't run on the template repo; enable it in
  your own repo.
- `scripts/setup-github-oidc.sh` — one-time IAM/OIDC role setup for the pipeline.
- `docs/CI-CD.md` — full guide to the deploy pipeline and how it ties to the
  version tags.

### Changed
- `docs/UPGRADES.md` rewritten around the lockstep tag / `whispa:imageTag` model;
  removed the stale GitHub Actions snippet (static AWS keys) in favour of the real
  OIDC workflow, and corrected the version-compatibility section.
- `docs/PREREQUISITES.md` — AWS Bedrock is the default keyless LLM (an
  OpenRouter/OpenAI key is no longer required); added AssemblyAI to the STT list.
- `README.md` — added the GitHub Actions deploy path and CI-CD docs link.

## [0.0.89] - 2026-06-24

## [0.0.88] - 2026-06-23

## [0.0.87] - 2026-06-23

## [0.0.86] - 2026-06-23

## [0.0.85] - 2026-06-22

## [0.0.84] - 2026-06-22

## [0.0.83] - 2026-06-21

## [0.0.82] - 2026-06-19

## [0.0.81] - 2026-06-18

## [0.0.80] - 2026-06-18

## [0.0.79] - 2026-06-18

## [0.0.78] - 2026-06-18

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
