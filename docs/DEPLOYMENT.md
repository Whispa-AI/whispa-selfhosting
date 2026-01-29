# Deployment Guide

This guide walks you through deploying Whispa to your AWS account.

## Overview

The deployment process:
1. Configure your Pulumi stack
2. Run `pulumi up` to create infrastructure
3. Configure DNS
4. Verify the deployment

**Estimated time**: 20-30 minutes

## Step 1: Clone the Repository

```bash
git clone https://github.com/Whispa-AI/whispa-selfhosting.git
cd whispa-selfhosting/infra/pulumi
```

## Step 2: Create Your Stack Configuration

Copy the example configuration:

```bash
cp Pulumi.dev.yaml.example Pulumi.prod.yaml
```

Edit `Pulumi.prod.yaml` with your settings:

```yaml
config:
  aws:region: us-east-1

  # Required settings
  whispa:environment: prod
  whispa:domain: whispa.yourcompany.com

  # API Keys (use Pulumi secrets for sensitive values)
  whispa:openRouterApiKey:
    secure: <encrypted-value>
  whispa:deepgramApiKey:
    secure: <encrypted-value>

  # Optional: Route 53 for automatic DNS
  whispa:route53ZoneId: Z1234567890ABC

  # Resource sizing (adjust based on expected load)
  whispa:dbInstanceClass: db.t3.small
  whispa:backendCpu: 512
  whispa:backendMemory: 1024
```

### Setting Secrets

For sensitive values, use Pulumi's secret management:

```bash
# Set secrets (they'll be encrypted)
pulumi config set --secret whispa:openRouterApiKey "your-api-key"
pulumi config set --secret whispa:deepgramApiKey "your-api-key"

# For optional services
pulumi config set --secret whispa:sentryDsn "your-sentry-dsn"
```

## Step 3: Initialize the Stack

```bash
# Login to Pulumi (if not already)
pulumi login

# Initialize your stack
pulumi stack init prod

# Preview the deployment
pulumi preview
```

The preview shows what resources will be created. Review it carefully.

## Step 4: Deploy

```bash
pulumi up
```

This will:
1. Create the VPC and networking
2. Set up the RDS PostgreSQL database
3. Create S3 bucket for audio storage
4. Deploy ECS services for backend and frontend
5. Configure the Application Load Balancer
6. Set up SSL/TLS certificates
7. Create IAM roles and policies

**This takes approximately 15-20 minutes** (RDS creation is the longest step).

## Step 5: Configure DNS

After deployment, Pulumi outputs the ALB DNS name:

```
Outputs:
    albDnsName: "whispa-prod-alb-123456789.us-east-1.elb.amazonaws.com"
    backendUrl: "https://api.whispa.yourcompany.com"
    frontendUrl: "https://whispa.yourcompany.com"
```

### If Using Route 53 (Automatic)

If you specified `route53ZoneId`, DNS records are created automatically.
Wait 2-5 minutes for DNS propagation.

### If Using External DNS

Create these DNS records in your DNS provider:

| Type | Name | Value |
|------|------|-------|
| CNAME | whispa.yourcompany.com | (ALB DNS name) |
| CNAME | api.whispa.yourcompany.com | (ALB DNS name) |

Also add the CNAME records for ACM certificate validation (shown in the Pulumi output).

## Step 6: Verify Deployment

### Check ECS Services

```bash
# View service status
aws ecs describe-services \
  --cluster whispa-prod \
  --services whispa-prod-backend whispa-prod-frontend \
  --query 'services[*].{name:serviceName,status:status,running:runningCount,desired:desiredCount}'
```

### Check Application Health

```bash
# Backend health check
curl https://api.whispa.yourcompany.com/health

# Expected response:
# {"status": "healthy"}
```

### Access the Application

Open your browser and navigate to:
- Frontend: `https://whispa.yourcompany.com`
- API docs: `https://api.whispa.yourcompany.com/docs`

## Step 7: Create Admin User

The first user to register will need admin access. After registration:

```bash
# Connect to the database (via bastion or SSM)
# Or use the seed script if provided by Whispa

# Set user as admin via SQL:
UPDATE users SET role = 'admin' WHERE email = 'your-email@company.com';
```

Or use the provided admin script:
```bash
# Run in ECS task or via SSM
python -m commands.set_admin_role --email your-email@company.com
```

## Troubleshooting Deployment

### Common Issues

**Certificate validation pending:**
- Ensure DNS CNAME records are created
- Wait up to 30 minutes for certificate validation

**ECS tasks failing to start:**
```bash
# Check task logs
aws logs get-log-events \
  --log-group-name /ecs/whispa-prod-backend \
  --log-stream-name $(aws logs describe-log-streams \
    --log-group-name /ecs/whispa-prod-backend \
    --order-by LastEventTime \
    --descending \
    --limit 1 \
    --query 'logStreams[0].logStreamName' \
    --output text)
```

**Database connection issues:**
- Check security group allows ECS tasks to connect
- Verify database credentials in Secrets Manager

See [TROUBLESHOOTING.md](TROUBLESHOOTING.md) for more solutions.

## Next Steps

- [CONFIGURATION.md](CONFIGURATION.md) - Customize your deployment
- [SECURITY.md](SECURITY.md) - Security best practices
- [UPGRADES.md](UPGRADES.md) - How to upgrade to new versions

## Destroying the Deployment

To remove all resources:

```bash
pulumi destroy
```

**Warning**: This will delete all data including the database. Export any data you need first.
