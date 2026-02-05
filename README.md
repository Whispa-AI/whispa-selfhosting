# Whispa Self-Hosting

Deploy Whispa to your own AWS infrastructure with full control over your data and configuration.

## Overview

This repository contains everything you need to deploy Whispa on AWS using Infrastructure as Code (Pulumi). The deployment creates a production-ready environment with:

- **ECS Fargate** for containerized backend and frontend services
- **RDS PostgreSQL** for the database
- **Application Load Balancer** with SSL/TLS termination
- **S3** for audio file storage
- **AWS Secrets Manager** for secure credential management
- **Route 53** for DNS management

## Quick Start

### Prerequisites

Before you begin, ensure you have:

- [ ] An AWS account with administrator access
- [ ] A registered domain name
- [ ] [Pulumi CLI](https://www.pulumi.com/docs/install/) installed
- [ ] [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) installed
- [ ] AWS CLI configured with credentials
- [ ] An LLM API key (OpenRouter recommended)

**Speech-to-text:** AWS Transcribe is recommended (uses IAM role, no API key needed). Alternatively, you can use Deepgram or ElevenLabs.

See [docs/PREREQUISITES.md](docs/PREREQUISITES.md) for detailed requirements.

### Deployment Steps

1. **Clone this repository**
   ```bash
   git clone https://github.com/Whispa-AI/whispa-selfhosting.git
   cd whispa-selfhosting
   ```

2. **Configure your stack**
   ```bash
   cd infra/pulumi
   cp Pulumi.dev.yaml.example Pulumi.<your-stack>.yaml
   # Edit the configuration file with your settings
   ```

3. **Deploy**
   ```bash
   pulumi login
   pulumi stack init <your-stack>
   pulumi up
   ```

4. **Configure DNS**
   - Point your domain to the ALB DNS name output by Pulumi
   - Or use Route 53 hosted zone (configured automatically if specified)

See [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) for the complete deployment guide.

## Documentation

| Document | Description |
|----------|-------------|
| [PREREQUISITES.md](docs/PREREQUISITES.md) | Required tools, accounts, and permissions |
| [DEPLOYMENT.md](docs/DEPLOYMENT.md) | Step-by-step deployment instructions |
| [CONFIGURATION.md](docs/CONFIGURATION.md) | All configuration options explained |
| [UPGRADES.md](docs/UPGRADES.md) | How to upgrade to new versions |
| [TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) | Common issues and solutions |
| [SECURITY.md](docs/SECURITY.md) | Security best practices |

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                           Internet                               │
└─────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────┐
│                    Application Load Balancer                     │
│                    (SSL/TLS Termination)                         │
└─────────────────────────────────────────────────────────────────┘
                    │                       │
                    ▼                       ▼
    ┌───────────────────────┐   ┌───────────────────────┐
    │   Frontend Service    │   │   Backend Service     │
    │   (ECS Fargate)       │   │   (ECS Fargate)       │
    │   Next.js App         │   │   FastAPI App         │
    └───────────────────────┘   └───────────────────────┘
                                        │
                    ┌───────────────────┼───────────────────┐
                    ▼                   ▼                   ▼
        ┌───────────────────┐ ┌─────────────────┐ ┌─────────────────┐
        │   RDS PostgreSQL  │ │   S3 Bucket     │ │ Secrets Manager │
        │   (Database)      │ │   (Audio Files) │ │ (Credentials)   │
        └───────────────────┘ └─────────────────┘ └─────────────────┘
```

## Configuration

Key configuration options in your Pulumi stack file:

```yaml
config:
  aws:region: ap-southeast-2
  whispa:domainName: whispa.yourcompany.com
  whispa:frontendUrl: https://whispa.yourcompany.com
  whispa:apiDomainName: api.whispa.yourcompany.com
  whispa:transcriptionProvider: amazon  # Uses AWS Transcribe (recommended)
  whispa:hostedZoneId: Z0123456789ABC   # Route53 for automatic DNS
  whispa:dbInstanceClass: db.t3.medium
  whispa:backendCpu: 512
  whispa:backendMemory: 1024
```

See [docs/CONFIGURATION.md](docs/CONFIGURATION.md) for all options.

## Costs

Estimated monthly costs (AWS us-east-1, minimal configuration):

| Resource | Specification | Est. Cost/Month |
|----------|---------------|-----------------|
| ECS Fargate (Backend) | 0.5 vCPU, 1GB RAM | ~$15 |
| ECS Fargate (Frontend) | 0.25 vCPU, 512MB RAM | ~$8 |
| RDS PostgreSQL | db.t3.micro | ~$15 |
| ALB | Standard | ~$20 |
| S3 | Pay per use | ~$1-5 |
| **Total** | | **~$60-65/month** |

Costs vary based on usage, region, and configuration choices.

## Support

- **Documentation**: Check the [docs/](docs/) folder
- **Issues**: [GitHub Issues](https://github.com/Whispa-AI/whispa-selfhosting/issues)
- **Email**: support@whispa.ai

## License

This repository is provided for Whispa customers under the terms of your Whispa license agreement.

## Version

Current version: See [CHANGELOG.md](CHANGELOG.md)

Compatible with Whispa images: `v1.0.0+`
