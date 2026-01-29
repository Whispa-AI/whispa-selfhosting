#!/bin/bash
# Whispa Database Backup Script
# Creates a backup of the Whispa database
#
# Prerequisites:
# - AWS CLI configured with appropriate credentials
# - Access to RDS instance
#
# Usage:
#   ./backup-db.sh <environment> [output-dir]
#
# Examples:
#   ./backup-db.sh prod
#   ./backup-db.sh prod /backups

set -e

ENVIRONMENT="${1:-prod}"
OUTPUT_DIR="${2:-.}"
TIMESTAMP=$(date +%Y%m%d-%H%M%S)
BACKUP_FILE="whispa-${ENVIRONMENT}-backup-${TIMESTAMP}.sql"

echo "=== Whispa Database Backup ==="
echo "Environment: $ENVIRONMENT"
echo "Output:      $OUTPUT_DIR/$BACKUP_FILE"
echo ""

# Get RDS endpoint from Pulumi
echo "Fetching RDS endpoint..."
cd "$(dirname "$0")/../infra/pulumi"

RDS_ENDPOINT=$(pulumi stack output dbEndpoint 2>/dev/null || echo "")
if [ -z "$RDS_ENDPOINT" ]; then
    echo "Error: Could not get RDS endpoint from Pulumi"
    echo "Make sure you're logged into Pulumi and the stack exists"
    exit 1
fi

# Get credentials from Secrets Manager
echo "Fetching credentials..."
SECRET_ID="whispa-${ENVIRONMENT}-db-credentials"
CREDENTIALS=$(aws secretsmanager get-secret-value --secret-id "$SECRET_ID" --query SecretString --output text 2>/dev/null || echo "")

if [ -z "$CREDENTIALS" ]; then
    echo "Error: Could not fetch credentials from Secrets Manager"
    echo "Secret ID: $SECRET_ID"
    exit 1
fi

DB_USER=$(echo "$CREDENTIALS" | jq -r '.username')
DB_PASSWORD=$(echo "$CREDENTIALS" | jq -r '.password')
DB_NAME=$(echo "$CREDENTIALS" | jq -r '.dbname // "whispa"')

echo "Creating backup..."
PGPASSWORD="$DB_PASSWORD" pg_dump \
    -h "$RDS_ENDPOINT" \
    -U "$DB_USER" \
    -d "$DB_NAME" \
    -F c \
    -f "$OUTPUT_DIR/$BACKUP_FILE"

if [ $? -eq 0 ]; then
    BACKUP_SIZE=$(ls -lh "$OUTPUT_DIR/$BACKUP_FILE" | awk '{print $5}')
    echo ""
    echo "=== Backup Complete ==="
    echo "File: $OUTPUT_DIR/$BACKUP_FILE"
    echo "Size: $BACKUP_SIZE"

    # Optionally upload to S3
    read -p "Upload to S3? (y/n) " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        S3_BUCKET="whispa-${ENVIRONMENT}-backups"
        echo "Uploading to s3://$S3_BUCKET/..."
        aws s3 cp "$OUTPUT_DIR/$BACKUP_FILE" "s3://$S3_BUCKET/database/"
        echo "Upload complete!"
    fi
else
    echo "Error: Backup failed"
    exit 1
fi
