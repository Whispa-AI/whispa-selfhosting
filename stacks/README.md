# stacks/

Per-environment Pulumi config for CI deploys.

The Pulumi project reads `infra/pulumi/Pulumi.<stack>.yaml`, but those files are
gitignored (so the public template repo never carries real config). For automated
deploys we keep the canonical copy here instead — one file per environment — and
the [`Deploy Whispa`](../.github/workflows/deploy.yml) workflow stages it into the
Pulumi project at deploy time:

```
stacks/dev.yaml   ──(workflow copies)──►   infra/pulumi/Pulumi.dev.yaml   ──►   pulumi up
```

This is the same split the internal `whispa-deployments` repo uses; here it lives
in one repo so you only manage one place.

## Getting started

```bash
cp stacks/dev.yaml.example stacks/dev.yaml      # then edit values
cp stacks/test.yaml.example stacks/test.yaml
```

Fill in the `CHANGE_ME` values. Set secrets with the CLI so they are encrypted
before they ever land in the file:

```bash
cd infra/pulumi
pulumi stack select dev
pulumi config set --secret whispa:superuserPassword "..."
# copy the resulting `secure:` block into ../../stacks/dev.yaml
```

## What is safe to commit

- ✅ Non-secret config and **encrypted** `secure:` values (encrypted with your
  KMS key or passphrase — useless without it).
- ❌ Plaintext API keys or passwords. Always use `pulumi config set --secret`.

> Keep the repo that holds your real `stacks/*.yaml` **private**. The example
> templates (`*.yaml.example`) are the only stack files committed upstream.

See [`docs/CI-CD.md`](../docs/CI-CD.md) for the full pipeline setup and
[`docs/CONFIGURATION.md`](../docs/CONFIGURATION.md) for every config key.
