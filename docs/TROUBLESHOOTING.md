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
1. Verify image exists: `docker pull ghcr.io/whispa-ai/whispa-backend:v1.0.0`
2. Check registry credentials if private
3. Ensure NAT gateway/VPC endpoints allow outbound traffic

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
