#!/usr/bin/env bash
#
# setup-github-oidc.sh — one-time IAM setup so the Deploy Whispa GitHub Action can
# assume an AWS role via OIDC (no long-lived AWS keys stored in GitHub).
#
# What it creates:
#   1. An IAM OIDC provider for token.actions.githubusercontent.com (idempotent —
#      skipped if it already exists).
#   2. An IAM role the workflow assumes, with its trust policy scoped to *your*
#      GitHub repo so no other repo can assume it.
#
# The role is granted AdministratorAccess because Pulumi for this project touches
# ECS, RDS, Lambda, Secrets Manager, ALB, VPC, Route53, ACM, IAM, CloudWatch, KMS,
# SES, EventBridge and S3. Tightening to least-privilege is a worthwhile follow-up
# once your deploys are stable; it is not required to get started.
#
# Usage (run with AWS credentials for the TARGET account, e.g. an admin/SSO session):
#   GH_REPO="your-org/your-private-deploy-repo" ./scripts/setup-github-oidc.sh
#
# Optional overrides:
#   ROLE_NAME   (default: whispa-pulumi-deploy)
#   AWS_REGION  (default: ap-southeast-2)  — only used in the printed hints
#
set -euo pipefail

GH_REPO="${GH_REPO:?set GH_REPO to your GitHub repo, e.g. your-org/whispa-deploy}"
ROLE_NAME="${ROLE_NAME:-whispa-pulumi-deploy}"
AWS_REGION="${AWS_REGION:-ap-southeast-2}"

ACCOUNT_ID="$(aws sts get-caller-identity --query Account --output text)"
OIDC_HOST="token.actions.githubusercontent.com"
OIDC_PROVIDER_ARN="arn:aws:iam::${ACCOUNT_ID}:oidc-provider/${OIDC_HOST}"

echo "==> AWS account: ${ACCOUNT_ID}"
echo "==> GitHub repo: ${GH_REPO}"
echo "==> Role name:   ${ROLE_NAME}"
echo

# 1. Ensure the GitHub OIDC provider exists (one per account).
if aws iam get-open-id-connect-provider --open-id-connect-provider-arn "$OIDC_PROVIDER_ARN" >/dev/null 2>&1; then
  echo "OIDC provider already exists, skipping."
else
  echo "Creating OIDC provider for ${OIDC_HOST}..."
  # Thumbprint is not validated by AWS for this provider, but the API requires one.
  aws iam create-open-id-connect-provider \
    --url "https://${OIDC_HOST}" \
    --client-id-list "sts.amazonaws.com" \
    --thumbprint-list "ffffffffffffffffffffffffffffffffffffffff" \
    >/dev/null
  echo "Created OIDC provider."
fi

# 2. Trust policy: only workflows in THIS repo may assume the role. The StringLike
#    on `sub` covers both branch runs (repo:ORG/REPO:ref:...) and, if you later add
#    GitHub Environments with reviewers, environment runs (repo:ORG/REPO:environment:...).
trust_policy() {
  cat <<EOF
{
  "Version": "2012-10-17",
  "Statement": [{
    "Effect": "Allow",
    "Principal": { "Federated": "${OIDC_PROVIDER_ARN}" },
    "Action": "sts:AssumeRoleWithWebIdentity",
    "Condition": {
      "StringEquals": { "${OIDC_HOST}:aud": "sts.amazonaws.com" },
      "StringLike":   { "${OIDC_HOST}:sub": "repo:${GH_REPO}:*" }
    }
  }]
}
EOF
}

if aws iam get-role --role-name "$ROLE_NAME" >/dev/null 2>&1; then
  echo "Role ${ROLE_NAME} already exists — updating its trust policy."
  aws iam update-assume-role-policy \
    --role-name "$ROLE_NAME" \
    --policy-document "$(trust_policy)"
else
  echo "Creating role ${ROLE_NAME}..."
  # 8h max session — a first-time stack provision can run 60+ minutes while ACM
  # certificate validation waits on DNS.
  aws iam create-role \
    --role-name "$ROLE_NAME" \
    --description "Pulumi deploy role for ${GH_REPO} (GitHub Actions OIDC)" \
    --assume-role-policy-document "$(trust_policy)" \
    --max-session-duration 28800 \
    --tags Key=purpose,Value=pulumi-deploy Key=repo,Value="${GH_REPO}" \
    >/dev/null
  aws iam attach-role-policy \
    --role-name "$ROLE_NAME" \
    --policy-arn arn:aws:iam::aws:policy/AdministratorAccess
  echo "Created role and attached AdministratorAccess."
fi

ROLE_ARN="arn:aws:iam::${ACCOUNT_ID}:role/${ROLE_NAME}"

echo
echo "================================================================"
echo "Done. Set these GitHub repository variables on ${GH_REPO}"
echo "(Settings -> Secrets and variables -> Actions -> Variables):"
echo
echo "  PULUMI_DEPLOY_ROLE_ARN = ${ROLE_ARN}"
echo "  AWS_REGION             = ${AWS_REGION}"
echo "  PULUMI_BACKEND_URL     = s3://<your-pulumi-state-bucket>"
echo
echo "If you store Pulumi secrets with a passphrase instead of AWS KMS, also add a"
echo "repository SECRET named PULUMI_CONFIG_PASSPHRASE (KMS needs no extra secret)."
echo "================================================================"
