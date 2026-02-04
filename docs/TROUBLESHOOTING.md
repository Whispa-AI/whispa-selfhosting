# Troubleshooting Guide

Common issues and their solutions when deploying and running Whispa.

## Deployment Issues

### Certificate Validation Pending

**Symptom**: Deployment hangs at certificate creation, or certificate shows "Pending validation"

**Cause**: DNS validation records not created or not propagated

**Solution**:
1. Check Pulumi output for required CNAME records
2. Add records to your DNS provider
3. Wait up to 30 minutes for propagation
4. Verify records: `dig _acme-challenge.yourdomain.com CNAME`

If using Route 53 with `route53ZoneId`, this should be automatic.

### ECS Tasks Failing to Start

**Symptom**: `pulumi up` completes but tasks show 0/1 running

**Check task status**:
```bash
aws ecs describe-tasks \
  --cluster whispa-prod \
  --tasks $(aws ecs list-tasks --cluster whispa-prod --query 'taskArns[0]' --output text)
```

**Check logs**:
```bash
aws logs tail /ecs/whispa-prod-backend --follow
```

**Common causes**:

1. **Image pull failure**
   - Check if image exists and tag is correct
   - Verify ECS task role has ECR/GHCR access

2. **Missing environment variables**
   - Check Secrets Manager has all required secrets
   - Verify task definition references correct secrets

3. **Database connection failure**
   - Check security group allows ECS → RDS connection
   - Verify database credentials in Secrets Manager

4. **Health check failure**
   - Application might be slow to start
   - Increase health check grace period

### Database Connection Errors

**Symptom**: Backend logs show "connection refused" or timeout errors

**Solutions**:

1. **Check security groups**:
   ```bash
   # Verify RDS security group allows inbound from ECS security group
   aws ec2 describe-security-groups --group-ids sg-xxx
   ```

2. **Verify credentials**:
   ```bash
   # Check secret exists
   aws secretsmanager get-secret-value --secret-id whispa-prod-db-credentials
   ```

3. **Test connectivity** (from bastion or SSM):
   ```bash
   psql -h <rds-endpoint> -U whispa -d whispa
   ```

### Secrets Already Scheduled for Deletion

**Symptom**: `pulumi up` fails with "secret is already scheduled for deletion"

**Cause**: Previous deployment attempt created secrets that were deleted but are in the 30-day recovery window

**Solution**: Force delete the secrets immediately:

```bash
aws secretsmanager delete-secret \
  --secret-id whispa/prod/app-secrets \
  --force-delete-without-recovery \
  --region ap-southeast-2

aws secretsmanager delete-secret \
  --secret-id whispa/prod/api-keys \
  --force-delete-without-recovery \
  --region ap-southeast-2

aws secretsmanager delete-secret \
  --secret-id whispa/prod/database-password \
  --force-delete-without-recovery \
  --region ap-southeast-2
```

Then run `pulumi up` again.

### NAT Gateway Errors

**Symptom**: Tasks can't reach external APIs (OpenRouter, Deepgram)

**Cause**: NAT Gateway not created or misconfigured

**Solution**:
```bash
# Verify NAT Gateway exists
aws ec2 describe-nat-gateways --filter "Name=state,Values=available"

# Check route table has 0.0.0.0/0 → NAT Gateway
aws ec2 describe-route-tables --filters "Name=vpc-id,Values=vpc-xxx"
```

## Runtime Issues

### CSRF Token Errors

**Symptom**: "CSRF token invalid" or "CSRF token missing" errors when making API calls

**Common causes**:

1. **Multiple browser tabs open**
   - One tab gets a new CSRF token, other tabs have stale cached tokens
   - **Fix**: Refresh the affected tab, or run in browser console:
     ```javascript
     localStorage.removeItem('whispa.csrf_token');
     location.reload();
     ```

2. **Cookie not being set**
   - Check browser DevTools → Application → Cookies for `csrf_token`
   - Ensure your domain is using HTTPS (required for secure cookies)

3. **Cross-origin issues**
   - If frontend and API are on different domains, cookies may not be shared
   - Ensure CORS is configured correctly

### LLM API Errors

**Symptom**: Call analysis fails, logs show OpenRouter/LLM errors

**Check**:
1. API key is valid and has credits
2. Model name is correct
3. Rate limits not exceeded

```bash
# Test API key
curl https://openrouter.ai/api/v1/models \
  -H "Authorization: Bearer $OPENROUTER_API_KEY"
```

### AWS Connect Permission Errors

**Symptom**: "AccessDeniedException: User is not authorized to perform: connect:ListUsers" or missing KVS permissions

**Cause**: AWS Connect integration not enabled or ECS task not restarted after config change

**Solution**:

1. **Set Connect instance ID** (this auto-enables all Connect permissions):
   ```bash
   pulumi config set whispa:connectInstanceId "your-connect-instance-id"
   pulumi up
   ```

2. **Restart the backend** to pick up new IAM permissions:
   ```bash
   aws ecs update-service \
     --cluster whispa-prod \
     --service whispa-prod-backend \
     --force-new-deployment \
     --region ap-southeast-2
   ```

3. **Verify IAM policies** were created:
   ```bash
   aws iam list-role-policies --role-name whispa-prod-ecs-task
   ```

   You should see `iam-task-connect` and `iam-task-kvs` in the list.

### Speech-to-Text Issues

**Symptom**: Transcription not working or empty

**For Deepgram**:
```bash
# Check API key
curl -X POST 'https://api.deepgram.com/v1/listen' \
  -H "Authorization: Token $DEEPGRAM_API_KEY" \
  -H "Content-Type: audio/wav" \
  --data-binary @test.wav
```

**For ElevenLabs**:
- Verify API key in Secrets Manager
- Check usage limits

### Slow Performance

**Symptoms**: High latency, timeouts

**Solutions**:

1. **Scale up compute**:
   ```bash
   pulumi config set whispa:backendCpu 1024
   pulumi config set whispa:backendMemory 2048
   pulumi up
   ```

2. **Add more tasks**:
   ```bash
   pulumi config set whispa:backendDesiredCount 2
   pulumi up
   ```

3. **Check database**:
   - Review slow query logs
   - Consider larger RDS instance

### Out of Memory

**Symptom**: Tasks killed with OOMKilled

**Solution**: Increase memory allocation:
```bash
pulumi config set whispa:backendMemory 2048
pulumi up
```

## Logging & Debugging

### View Container Logs

```bash
# Recent logs
aws logs tail /ecs/whispa-prod-backend

# Follow logs
aws logs tail /ecs/whispa-prod-backend --follow

# Specific time range
aws logs filter-log-events \
  --log-group-name /ecs/whispa-prod-backend \
  --start-time $(date -d '1 hour ago' +%s000)
```

### Connect to Container

```bash
# Enable ECS Exec (if not already)
aws ecs update-service \
  --cluster whispa-prod \
  --service whispa-prod-backend \
  --enable-execute-command

# Connect
aws ecs execute-command \
  --cluster whispa-prod \
  --task <task-id> \
  --container backend \
  --interactive \
  --command "/bin/bash"
```

### Database Queries

```bash
# Connect to database
aws ecs execute-command \
  --cluster whispa-prod \
  --task <task-id> \
  --container backend \
  --interactive \
  --command "python -c \"from src.core.database import engine; print(engine.url)\""
```

## Common Error Messages

### "No container instances to place task"

**Cause**: Using EC2 launch type instead of Fargate, or no available capacity

**Solution**: Ensure task definition uses `FARGATE` launch type

### "ResourceInitializationError: unable to pull secrets"

**Cause**: ECS task role doesn't have permission to read secrets

**Solution**: Check IAM role attached to task has `secretsmanager:GetSecretValue` permission

### "CannotPullContainerError"

**Cause**: Can't pull image from registry

**Solutions**:

1. **Verify image exists**:
   ```bash
   docker pull ghcr.io/whispa-ai/whispa-backend:latest
   ```

2. **Check if images are public**: Go to https://github.com/orgs/Whispa-AI/packages and ensure packages have public visibility

3. **Build and push your own images** (if needed):
   ```bash
   # Login to GHCR
   echo $GITHUB_TOKEN | docker login ghcr.io -u USERNAME --password-stdin

   # Build for linux/amd64 (required for ECS Fargate)
   docker build --platform linux/amd64 -t ghcr.io/your-org/whispa-backend:latest ./backend
   docker build --platform linux/amd64 -t ghcr.io/your-org/whispa-frontend:latest ./frontend

   # Push
   docker push ghcr.io/your-org/whispa-backend:latest
   docker push ghcr.io/your-org/whispa-frontend:latest

   # Update Pulumi config
   pulumi config set whispa:backendImage "ghcr.io/your-org/whispa-backend:latest"
   pulumi config set whispa:frontendImage "ghcr.io/your-org/whispa-frontend:latest"
   pulumi up
   ```

4. **Ensure NAT gateway allows outbound traffic** to ghcr.io

### "Connection timed out" to external APIs

**Cause**: No outbound internet access

**Solutions**:
1. Verify NAT Gateway is running
2. Check route tables have 0.0.0.0/0 route to NAT
3. Verify security group allows outbound traffic

## Getting Help

If you can't resolve an issue:

1. **Collect diagnostic information**:
   ```bash
   # Get Pulumi state
   pulumi stack export > state.json

   # Get recent logs
   aws logs tail /ecs/whispa-prod-backend --since 1h > backend-logs.txt

   # Get service status
   aws ecs describe-services --cluster whispa-prod --services whispa-prod-backend > service-status.json
   ```

2. **Open an issue**: [GitHub Issues](https://github.com/Whispa-AI/whispa-selfhosting/issues)

3. **Contact support**: support@whispa.ai
