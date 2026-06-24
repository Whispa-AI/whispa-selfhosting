# Deploying with GitHub Actions

This repo ships an example **manual deploy pipeline** so you can roll out a new
Whispa version to your `dev` and `test` environments from the GitHub UI — pick the
environment, type the version, press **Run workflow**. Under the hood it runs the
same `pulumi up` you'd run locally.

It mirrors how Whispa operates its own customer environments (the internal
`whispa-deployments` repo), collapsed into this single repo so you manage one place.

```
You: Actions ▸ Deploy Whispa ▸ Run workflow (stack=dev, version=0.0.89)
        │
        ▼
  checkout ─► stage stacks/dev.yaml ─► AWS OIDC role ─► pulumi up (imageTag=0.0.89)
```

- Workflow: [`.github/workflows/deploy.yml`](../.github/workflows/deploy.yml)
- Stack config: [`stacks/`](../stacks/) (see [stacks/README.md](../stacks/README.md))
- OIDC setup script: [`scripts/setup-github-oidc.sh`](../scripts/setup-github-oidc.sh)

> **The workflow ships disabled (fully commented out)** so it never runs on the
> upstream template repo. Enable it in your own repo as part of the
> [one-time setup](#one-time-setup) below.

---

## How it works

1. You start the **Deploy Whispa** workflow and choose a `stack` (`dev` / `test`)
   and a `version` (e.g. `0.0.89`).
2. The workflow copies `stacks/<stack>.yaml` to `infra/pulumi/Pulumi.<stack>.yaml`
   (the Pulumi project's config files are gitignored, so they're kept in `stacks/`
   and staged at deploy time).
3. It assumes an AWS IAM role via **OIDC** — no static AWS keys live in GitHub.
4. It runs `pulumi up` in `infra/pulumi/`, passing your version as
   `whispa:imageTag`. The backend and frontend both deploy at that version.

The `version` you type is applied **for that run only** — it is not committed back.
To make a version sticky for an environment, set `whispa:imageTag` in
`stacks/<stack>.yaml` and commit it. Leave the `version` box blank to deploy the
version baked into the checkout (see [Versioning](#versioning--what-to-type-in-version)).

---

## One-time setup

You need this once per AWS account / GitHub repo. Estimated time: ~15 minutes.

### 0. Use a private repo

Your real `stacks/*.yaml` live in this repo, so keep your copy **private**. The
encrypted `secure:` values are safe to commit; plaintext secrets are never
committed (always use `pulumi config set --secret`).

### 1. Pulumi state backend (S3)

The workflow stores Pulumi state in an S3 bucket (no Pulumi SaaS account needed —
better for data residency). Create one bucket per account, reused by all stacks:

```bash
aws s3 mb s3://your-company-whispa-pulumi-state --region ap-southeast-2
```

The URL becomes the `PULUMI_BACKEND_URL` variable below.

### 2. Secrets provider (AWS KMS — recommended)

With an S3 backend, Pulumi encrypts `secure:` config values with a provider you
choose. AWS KMS is recommended because the deploy role can decrypt with **no extra
GitHub secret**:

```bash
aws kms create-alias \
  --alias-name alias/pulumi-secrets \
  --target-key-id "$(aws kms create-key --query KeyMetadata.KeyId --output text)" \
  --region ap-southeast-2
```

You'll point each stack at it when you init it (next section), e.g.
`--secrets-provider "awskms://alias/pulumi-secrets?region=ap-southeast-2"`.

> Prefer a passphrase instead of KMS? Then add a repository **secret**
> `PULUMI_CONFIG_PASSPHRASE` and the workflow will pick it up. KMS needs no extra
> secret. (The internal repo's `migrate-to-kms.sh` shows a passphrase→KMS move.)

### 3. IAM role for GitHub OIDC

Run the helper with credentials for the target AWS account:

```bash
GH_REPO="your-org/your-private-deploy-repo" ./scripts/setup-github-oidc.sh
```

It creates (idempotently) the GitHub OIDC provider and an IAM role whose trust
policy is scoped to **only your repo**, then prints the exact variable values to
set next. (It attaches `AdministratorAccess` — fine to start; tighten later.)

### 4. GitHub repository variables

In your repo: **Settings → Secrets and variables → Actions → Variables**, add:

| Variable | Example | Notes |
|----------|---------|-------|
| `PULUMI_DEPLOY_ROLE_ARN` | `arn:aws:iam::123456789012:role/whispa-pulumi-deploy` | Printed by the setup script |
| `AWS_REGION` | `ap-southeast-2` | Your deploy region |
| `PULUMI_BACKEND_URL` | `s3://your-company-whispa-pulumi-state` | From step 1 |

Only add the `PULUMI_CONFIG_PASSPHRASE` **secret** if you chose a passphrase in
step 2.

### 5. Create your stack config + first deploy (locally)

Do the very first deploy from your laptop — it provisions DNS, certificates, and
the database, and ACM validation often needs interactive waiting. After that, the
button handles version rollouts.

```bash
cp stacks/dev.yaml.example stacks/dev.yaml      # edit the CHANGE_ME values

cd infra/pulumi
cp ../../stacks/dev.yaml Pulumi.dev.yaml
export PULUMI_BACKEND_URL=s3://your-company-whispa-pulumi-state
pulumi stack init dev --secrets-provider "awskms://alias/pulumi-secrets?region=ap-southeast-2"
pulumi config set --secret whispa:superuserPassword "..."
pulumi up
```

Copy the final `Pulumi.dev.yaml` (now carrying the `encryptedkey:` and any
`secure:` blocks) back to `stacks/dev.yaml` and commit it. See
[DEPLOYMENT.md](DEPLOYMENT.md) for the full first-time walkthrough.

### 6. Enable the workflow

The workflow ships **commented out** so it doesn't run on the upstream repo.
In your repo, enable it by stripping the leading `# ` from the body of
`.github/workflows/deploy.yml` (everything under the header), then commit. After
that it appears under the **Actions** tab as **Deploy Whispa**.

---

## Running a deploy

1. Go to the repo's **Actions** tab → **Deploy Whispa** → **Run workflow**.
2. (Optional) In **Use workflow from**, pick a branch or a release tag. Running
   from a release tag `vX.Y.Z` deploys that release's infra; leaving `version`
   blank then deploys app `X.Y.Z` too (full lockstep).
3. Choose the **stack** (`dev` or `test`).
4. Enter the **version**, e.g. `0.0.89` (just the number — not a full image path).
5. **Run workflow**. Watch the `Pulumi up` step for the resource diff and outputs.

---

## Versioning — what to type in `version`

Whispa publishes releases as tags `vX.Y.Z`. Each release builds matching app
images: tag `v0.0.89` ⇒ `whispa-backend:0.0.89` and `whispa-frontend:0.0.89`.

- The **CHANGELOG** ([../CHANGELOG.md](../CHANGELOG.md)) and the
  [Releases page](https://github.com/Whispa-AI/whispa-selfhosting/releases) list
  available versions and what changed.
- Type that number (without the leading `v`) into the `version` box: `0.0.89`.
- Internally the workflow sets `whispa:imageTag`, which both services use. See
  [UPGRADES.md](UPGRADES.md) for the upgrade/rollback model and
  [CONFIGURATION.md](CONFIGURATION.md#app-version-container-images) for the image
  resolution precedence.

**Lockstep, briefly:** the infra you check out has a default app version baked in
(`infra/pulumi/Config/Version.cs`). Entering a `version` overrides it for that
deploy; leaving it blank uses the baked-in default. For strict infra+app lockstep,
run the workflow from the matching release tag and leave `version` blank.

---

## Rollback

Re-run the workflow with the previous good version:

```
Deploy Whispa ▸ stack=dev ▸ version=0.0.88 ▸ Run workflow
```

ECS rolls task definitions back to that image. Database migrations are applied on
startup and are forward-only — review release notes before crossing a migration
boundary. See [UPGRADES.md](UPGRADES.md#rollback-procedure).

---

## Extending to production

This example intentionally covers only `dev` and `test`. To add a gated `prod`
deploy later:

1. Add a `stacks/prod.yaml` and a `prod` option to the workflow's `stack` input
   (or a separate `deploy-prod.yml`).
2. Put the job behind a GitHub **Environment** named `prod` with **required
   reviewers** (Settings → Environments). Deploys then pause for approval.
3. Optionally scope a separate IAM role to `repo:<org>/<repo>:environment:prod`
   so the approval gate is enforced at the AWS layer too.

This is exactly how Whispa runs its own customer-prod deploys.

---

## Troubleshooting

| Symptom | Likely cause / fix |
|---------|--------------------|
| `repository variable for X is not set` | Add the [repo variables](#4-github-repository-variables). |
| `stacks/<stack>.yaml not found` | Create it from the `.example` and commit it. |
| `Not authorized to perform sts:AssumeRoleWithWebIdentity` | `GH_REPO` in the setup script didn't match the repo, or repo is the wrong owner. Re-run the script. |
| `error: getting secrets manager: passphrase must be set` | Stack uses a passphrase provider — add the `PULUMI_CONFIG_PASSPHRASE` secret, or migrate the stack to KMS. |
| `no stack named '<stack>'` | Do the first `pulumi stack init` + deploy locally (step 5). |

More general issues: [TROUBLESHOOTING.md](TROUBLESHOOTING.md).
