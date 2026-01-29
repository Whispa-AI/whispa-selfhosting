# Whispa AWS Connect Lambda

This Lambda function integrates AWS Connect with Whispa for real-time call transcription and analysis.

## Overview

When a call starts in AWS Connect, this Lambda is invoked from a Contact Flow. It extracts call metadata (contact ID, agent info, customer number) and forwards it to the Whispa backend, which then starts consuming the Kinesis Video Stream (KVS) for audio transcription.

## Deployment Options

### Option 1: Pulumi (Recommended)

If you're deploying with Pulumi, the Lambda is automatically created when `enableAwsConnect` is true:

```yaml
# Pulumi.prod.yaml
config:
  whispa:enableAwsConnect: true
  whispa:connectInstanceId: "your-connect-instance-id"
  whispa:connectApiKey: "optional-api-key"  # For authentication
```

The Lambda ARN will be output as `connectLambdaArn`.

### Option 2: Manual Deployment

Use the deploy script for standalone deployment:

```bash
./deploy.sh https://api.your-whispa.com your-api-key ap-southeast-2 whispa-connect
```

Arguments:
- `WHISPA_API_URL` - Your Whispa backend URL (required)
- `WHISPA_API_KEY` - API key for authentication (optional but recommended)
- `AWS_REGION` - AWS region (default: ap-southeast-2)
- `FUNCTION_NAME` - Lambda function name (default: whispa-connect)

## Architecture

```
Customer Call
     |
     v
AWS Connect Contact Flow
     |
     +-> Start Media Streaming (creates KVS stream)
     |
     +-> Invoke Lambda (this function)
              |
              v
         Whispa Backend --> Consumes KVS --> Transcription
```

## Contact Flow Setup

1. In AWS Connect admin console, create or edit a Contact Flow

2. Add blocks in this order:
   - **Start media streaming** - Creates the KVS stream (REQUIRED first)
   - **Invoke AWS Lambda function** - Select your Lambda
   - **Set working queue** and **Transfer to queue** - Or your normal call routing

3. Publish the Contact Flow

4. Assign to phone number(s)

## Whispa Configuration

After deploying the Lambda, configure Whispa:

1. Go to **Settings > Integrations > Amazon Connect**
2. Enter your AWS Region and Connect Instance ID
3. Map AWS Connect agents to Whispa users
4. Enable the integration

## Resource Naming

When using Pulumi with a custom `resourcePrefix` (e.g., `whispa-dev`), the Lambda will be named `{prefix}-connect-lambda`.

Examples:
- `resourcePrefix: whispa-dev` → `whispa-dev-connect-lambda`
- `resourcePrefix: acme-prod` → `acme-prod-connect-lambda`

## IAM Permissions

### Lambda Execution Role

The Lambda needs:
- `AWSLambdaBasicExecutionRole` (for CloudWatch logs)

### Whispa Backend Role

The Whispa backend needs permission to read from KVS (automatically configured by Pulumi when `enableAwsConnect` is true):

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "kinesisvideo:GetMedia",
        "kinesisvideo:GetDataEndpoint",
        "kinesisvideo:DescribeStream"
      ],
      "Resource": "arn:aws:kinesisvideo:*:*:stream/your-kvs-prefix-*"
    }
  ]
}
```

## Troubleshooting

### Call not appearing in Whispa

1. Check Lambda CloudWatch logs for errors
2. Verify `WHISPA_API_URL` is correct and accessible from Lambda
3. Ensure agent is mapped in Whispa UI
4. Check "Start media streaming" block runs BEFORE the Lambda

### "Missing stream_arn" error

The "Start media streaming" block must run before the Lambda invocation in the Contact Flow.

### Connection timeout

- Increase Lambda timeout (default: 30s should be sufficient)
- Verify Whispa backend is accessible from Lambda VPC (if using VPC)
