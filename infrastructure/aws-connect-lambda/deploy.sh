#!/bin/bash
# Deploy the Whispa AWS Connect Lambda
#
# Prerequisites:
#   - AWS CLI configured with appropriate permissions
#   - Lambda execution role created (or let this script create one)
#
# Usage:
#   ./deploy.sh <WHISPA_API_URL> [WHISPA_API_KEY] [AWS_REGION] [FUNCTION_NAME]
#
# Example:
#   ./deploy.sh https://api.whispa.io my-api-key ap-southeast-2 whispa-connect
#
# After deployment, grant AWS Connect permission to invoke this Lambda:
#   aws lambda add-permission \
#     --function-name whispa-connect \
#     --statement-id AllowConnect \
#     --action lambda:InvokeFunction \
#     --principal connect.amazonaws.com \
#     --source-account YOUR_AWS_ACCOUNT_ID

set -e

# Configuration
WHISPA_API_URL="${1:?Usage: ./deploy.sh <WHISPA_API_URL> [WHISPA_API_KEY] [AWS_REGION] [FUNCTION_NAME]}"
WHISPA_API_KEY="${2:-}"
AWS_REGION="${3:-ap-southeast-2}"
FUNCTION_NAME="${4:-whispa-connect}"
ROLE_NAME="${FUNCTION_NAME}-role"

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
TEMP_DIR=$(mktemp -d)
ZIP_FILE="${TEMP_DIR}/lambda.zip"

echo "=== Whispa AWS Connect Lambda Deployment ==="
echo "Region: ${AWS_REGION}"
echo "Function: ${FUNCTION_NAME}"
echo "Whispa URL: ${WHISPA_API_URL}"
echo ""

# Check if role exists, create if not
echo "Checking IAM role..."
if ! aws iam get-role --role-name "${ROLE_NAME}" --region "${AWS_REGION}" 2>/dev/null; then
    echo "Creating IAM role ${ROLE_NAME}..."
    aws iam create-role \
        --role-name "${ROLE_NAME}" \
        --assume-role-policy-document "file://${SCRIPT_DIR}/trust-policy.json" \
        --region "${AWS_REGION}"

    # Attach basic Lambda execution policy
    aws iam attach-role-policy \
        --role-name "${ROLE_NAME}" \
        --policy-arn "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole" \
        --region "${AWS_REGION}"

    # Wait for role to propagate
    echo "Waiting for role to propagate..."
    sleep 10
fi

ROLE_ARN=$(aws iam get-role --role-name "${ROLE_NAME}" --query 'Role.Arn' --output text)
echo "Using role: ${ROLE_ARN}"

# Create deployment package
echo ""
echo "Creating deployment package..."
cp "${SCRIPT_DIR}/lambda_function.py" "${TEMP_DIR}/"
cd "${TEMP_DIR}"
zip -j "${ZIP_FILE}" lambda_function.py
echo "Package created: ${ZIP_FILE}"

# Build environment variables JSON
ENV_VARS="{\"WHISPA_API_URL\":\"${WHISPA_API_URL}\""
if [ -n "${WHISPA_API_KEY}" ]; then
    ENV_VARS="${ENV_VARS},\"WHISPA_API_KEY\":\"${WHISPA_API_KEY}\""
fi
ENV_VARS="${ENV_VARS}}"

# Check if function exists
echo ""
if aws lambda get-function --function-name "${FUNCTION_NAME}" --region "${AWS_REGION}" 2>/dev/null; then
    echo "Updating existing Lambda function..."

    # Update code
    aws lambda update-function-code \
        --function-name "${FUNCTION_NAME}" \
        --zip-file "fileb://${ZIP_FILE}" \
        --region "${AWS_REGION}"

    # Wait for update to complete
    aws lambda wait function-updated \
        --function-name "${FUNCTION_NAME}" \
        --region "${AWS_REGION}"

    # Update environment variables
    aws lambda update-function-configuration \
        --function-name "${FUNCTION_NAME}" \
        --environment "Variables=${ENV_VARS}" \
        --region "${AWS_REGION}"
else
    echo "Creating new Lambda function..."
    aws lambda create-function \
        --function-name "${FUNCTION_NAME}" \
        --runtime python3.12 \
        --handler lambda_function.lambda_handler \
        --role "${ROLE_ARN}" \
        --zip-file "fileb://${ZIP_FILE}" \
        --timeout 30 \
        --memory-size 128 \
        --environment "Variables=${ENV_VARS}" \
        --region "${AWS_REGION}"
fi

# Get function ARN
FUNCTION_ARN=$(aws lambda get-function --function-name "${FUNCTION_NAME}" --region "${AWS_REGION}" --query 'Configuration.FunctionArn' --output text)

# Cleanup
rm -rf "${TEMP_DIR}"

echo ""
echo "=== Deployment Complete ==="
echo ""
echo "Lambda ARN: ${FUNCTION_ARN}"
echo ""
echo "Next steps:"
echo "1. Grant AWS Connect permission to invoke this Lambda:"
echo ""
echo "   aws lambda add-permission \\"
echo "     --function-name ${FUNCTION_NAME} \\"
echo "     --statement-id AllowConnect \\"
echo "     --action lambda:InvokeFunction \\"
echo "     --principal connect.amazonaws.com \\"
echo "     --source-account YOUR_AWS_ACCOUNT_ID \\"
echo "     --region ${AWS_REGION}"
echo ""
echo "2. In AWS Connect Contact Flow:"
echo "   - Add 'Start media streaming' block FIRST"
echo "   - Add 'Invoke AWS Lambda function' block and select ${FUNCTION_NAME}"
echo "   - Continue with queue/transfer"
echo ""
