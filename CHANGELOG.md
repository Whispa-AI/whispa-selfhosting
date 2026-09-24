# Changelog

All notable changes to the Whispa Self-Hosting repository will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

> **Versioning:** from the next release onward, this repo tracks the **product
> version** (the `0.0.x` line shared with the `whispa-backend`/`whispa-frontend`
> images), so an infra tag `v0.0.N` deploys app images `0.0.N`. This supersedes the
> standalone `1.0.0` baseline below.

## [Unreleased]

## [0.0.147] - 2026-09-24

### Fixed
- The rollout check added in 0.0.146 now runs on Windows. It was a bash script
  calling the AWS CLI, so `pulumi up` failed on Windows machines and CI runners
  ("'bash' is not recognized"). It is now a small .NET tool (`infra/rollout-gate`)
  run with `dotnet`, which Pulumi already requires, using the AWS SDK with the same
  credentials: no bash or AWS CLI needed.

## [0.0.146] - 2026-09-23

### Fixed
- `pulumi up` now fails when an ECS service rolls back. The deployment circuit
  breaker rolled failed releases back after `pulumi up` had already reported
  success, leaving a new frontend on the previous backend without any signal. A
  rollout check (`scripts/wait-for-ecs-rollout.sh`, needs the AWS CLI) now waits
  for each new task definition to finish rolling out.
- The frontend service updates only after the backend has rolled out, so a failed
  backend no longer leaves the two on different releases.

### Added
- Warning when the backend and frontend images resolve to different versions.
- The example deploy workflow runs `pulumi up --refresh` (so a retry after a
  rollback redeploys) and checks that `/health` reports the deployed version.
- Failed-deployment alert: an EventBridge rule sends ECS `SERVICE_DEPLOYMENT_FAILED`
  events for the backend and frontend to the alert topic, so a release the
  circuit breaker rolled back no longer goes unnoticed. On by default when
  `alarmEmailAddress` or `alarmSnsTopicArn` is set; `whispa:enableDeploymentAlerts`
  turns it off. The Pulumi-created topic gets a policy allowing EventBridge to
  publish; an existing `alarmSnsTopicArn` topic must allow it itself.

### Changed
- Docs: Bedrock models need a one-time Marketplace subscription per AWS account
  (the task role can't do it). Previously the docs said Bedrock needed no setup,
  which let an unsubscribed Claude Sonnet 4.6 roll back an upgrade. The docs now
  list every model family in the recommended set and explain how the backend's
  startup check reports a model it can't reach.

## [0.0.145] - 2026-09-21

## [0.0.144] - 2026-09-08

## [0.0.143] - 2026-09-08

## [0.0.142] - 2026-09-02

## [0.0.141] - 2026-09-01

## [0.0.140] - 2026-08-23

## [0.0.139] - 2026-08-23

## [0.0.138] - 2026-08-22

## [0.0.137] - 2026-08-22

## [0.0.136] - 2026-08-21

## [0.0.135] - 2026-08-20

## [0.0.134] - 2026-08-20

## [0.0.133] - 2026-08-11

## [0.0.132] - 2026-08-04

## [0.0.131] - 2026-07-31

## [0.0.130] - 2026-07-31

## [0.0.129] - 2026-07-30

## [0.0.128] - 2026-07-30

## [0.0.127] - 2026-07-29

## [0.0.126] - 2026-07-29

## [0.0.125] - 2026-07-27

## [0.0.124] - 2026-07-27

## [0.0.123] - 2026-07-22

## [0.0.122] - 2026-07-21

## [0.0.121] - 2026-07-21

## [0.0.120] - 2026-07-15

## [0.0.119] - 2026-07-15

## [0.0.118] - 2026-07-14

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
