# Prerequisites

Before deploying Whispa, ensure you have the following requirements met.

## Required Tools

### 1. Pulumi CLI

Pulumi is used for infrastructure as code. Install it:

**macOS:**
```bash
brew install pulumi/tap/pulumi
```

**Windows:**
```powershell
choco install pulumi
```

**Linux:**
```bash
curl -fsSL https://get.pulumi.com | sh
```

Verify installation:
```bash
pulumi version
# Should output: v3.x.x or higher
```

### 2. .NET 8 SDK

The Pulumi program is written in C#. Install .NET 8:

**macOS:**
```bash
brew install dotnet@8
```

**Windows:**
Download from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/8.0)

**Linux:**
```bash
# Ubuntu/Debian
sudo apt-get install -y dotnet-sdk-8.0

# Fedora
sudo dnf install dotnet-sdk-8.0
```

Verify installation:
```bash
dotnet --version
# Should output: 8.x.x
```

### 3. AWS CLI

Install and configure the AWS CLI:

**macOS:**
```bash
brew install awscli
```

**Windows:**
```powershell
msiexec.exe /i https://awscli.amazonaws.com/AWSCLIV2.msi
```

**Linux:**
```bash
curl "https://awscli.amazonaws.com/awscli-exe-linux-x86_64.zip" -o "awscliv2.zip"
unzip awscliv2.zip
sudo ./aws/install
```

Configure with your credentials:
```bash
aws configure
# Enter your AWS Access Key ID, Secret Access Key, and default region
```

## AWS Account Requirements

### Required Permissions

Your AWS user/role needs permissions to create:

- **VPC & Networking**: VPCs, subnets, security groups, NAT gateways
- **ECS**: Clusters, services, task definitions
- **RDS**: Database instances, subnet groups
- **S3**: Buckets and policies
- **IAM**: Roles and policies for ECS tasks
- **ACM**: SSL/TLS certificates
- **Route 53**: Hosted zones and records (if using Route 53)
- **Secrets Manager**: Secrets for credentials
- **CloudWatch**: Log groups for container logs

For initial setup, we recommend using an IAM user with `AdministratorAccess`. After deployment, you can create a more restricted policy.

### Service Quotas

Ensure your AWS account has sufficient quotas:

| Service | Resource | Minimum Required |
|---------|----------|------------------|
| VPC | VPCs per region | 1 |
| VPC | Elastic IPs | 2 (for NAT gateways) |
| ECS | Fargate tasks | 2 |
| RDS | DB instances | 1 |
| S3 | Buckets | 1 |

### Regions

Whispa can be deployed to any AWS region that supports:
- ECS Fargate
- RDS PostgreSQL
- Application Load Balancer

Recommended regions:
- `us-east-1` (N. Virginia) - Lowest latency to most services
- `us-west-2` (Oregon)
- `eu-west-1` (Ireland)
- `ap-southeast-2` (Sydney)

## Domain Requirements

You need a domain name for your Whispa deployment. Options:

### Option 1: Route 53 Hosted Zone (Recommended)

If your domain is managed in Route 53:
1. Note your hosted zone ID
2. Pulumi will automatically create DNS records and SSL certificates

### Option 2: External DNS

If your domain is managed elsewhere:
1. Pulumi will create an ACM certificate
2. You'll need to add CNAME records for certificate validation
3. After deployment, point your domain to the ALB

## Pulumi Account

You need a Pulumi account to store state:

1. Create a free account at [app.pulumi.com](https://app.pulumi.com)
2. Or use a self-managed backend (S3, local filesystem)

```bash
# Login to Pulumi Cloud (recommended)
pulumi login

# Or use local state (for testing)
pulumi login --local

# Or use S3 backend
pulumi login s3://your-pulumi-state-bucket
```

## API Keys & Services

Depending on your configuration, you may need:

| Service | Purpose | Required? |
|---------|---------|-----------|
| OpenRouter / OpenAI | LLM for call analysis | Yes |
| **Speech-to-text** (choose one): | | |
| ↳ AWS Transcribe | Uses IAM role, no API key needed | Recommended for AWS |
| ↳ Deepgram | External STT provider | Alternative |
| ↳ ElevenLabs | External STT provider | Alternative |
| Sentry | Error tracking | No |
| Langfuse | LLM observability | No |

**Note:** AWS Transcribe is recommended for self-hosting on AWS since it uses your ECS task role for authentication — no external API keys to manage.

## Checklist

Before proceeding to deployment, confirm:

- [ ] Pulumi CLI installed and logged in
- [ ] .NET 8 SDK installed
- [ ] AWS CLI installed and configured
- [ ] AWS account with sufficient permissions
- [ ] Domain name available
- [ ] OpenRouter/OpenAI API key obtained
- [ ] Speech-to-text ready: AWS Transcribe (no key needed) OR Deepgram/ElevenLabs API key

## Next Steps

Once all prerequisites are met, proceed to [DEPLOYMENT.md](DEPLOYMENT.md).
