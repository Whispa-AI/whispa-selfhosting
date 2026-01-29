using Pulumi;
using Whispa.Aws.Pulumi.Configuration;
using Whispa.Aws.Pulumi.Components;

return await Deployment.RunAsync(() =>
{
    // Load configuration
    var config = new WhispaConfig();

    var apiDomain = string.IsNullOrWhiteSpace(config.ApiDomainName) ? config.DomainName : config.ApiDomainName;

    if (!config.AutoCertificate && string.IsNullOrWhiteSpace(config.CertificateArn))
    {
        throw new InvalidOperationException(
            "certificateArn is required unless autoCertificate is enabled.");
    }

    if (config.AutoCertificate && string.IsNullOrWhiteSpace(config.HostedZoneId))
    {
        throw new InvalidOperationException(
            "autoCertificate requires hostedZoneId to be set.");
    }

    // ===================
    // Phase 1: Foundation
    // ===================

    // Networking (VPC, subnets, security groups)
    var networking = new NetworkingStack("networking", config);

    // Secrets (auto-generated passwords and API keys)
    var secrets = new SecretsStack("secrets", config);

    // ===================
    // Phase 2: Data Layer
    // ===================

    // Database (RDS PostgreSQL)
    var database = new DatabaseStack("database", config,
        privateSubnetIds: networking.PrivateSubnetIds,
        securityGroupId: networking.RdsSecurityGroupId,
        dbPassword: secrets.DbPassword);

    // Storage (S3 buckets)
    var storage = new StorageStack("storage", config);

    // ===================
    // Phase 3: Supporting Services
    // ===================

    // Monitoring (CloudWatch log groups)
    var monitoring = new MonitoringStack("monitoring", config);

    // IAM (roles and policies)
    var iam = new IamStack("iam", config,
        audioBucketArn: storage.AudioBucketArn,
        dbPasswordSecretArn: secrets.DbPasswordSecretArn,
        appSecretsArn: secrets.AppSecretsArn,
        apiKeysSecretArn: secrets.ApiKeysSecretArn,
        backendLogGroupArn: monitoring.BackendLogGroupArn,
        frontendLogGroupArn: monitoring.FrontendLogGroupArn);

    // ===================
    // Phase 4: Compute
    // ===================

    var certificateArn = config.AutoCertificate
        ? new CertificateStack("certificate", config, config.HostedZoneId!).CertificateArn
        : Output.Create(config.CertificateArn!);

    // ECS Fargate + ALB
    var compute = new ComputeStack("compute", config,
        certificateArn: certificateArn,
        vpcId: networking.VpcId,
        publicSubnetIds: networking.PublicSubnetIds,
        privateSubnetIds: networking.PrivateSubnetIds,
        albSecurityGroupId: networking.AlbSecurityGroupId,
        ecsSecurityGroupId: networking.EcsSecurityGroupId,
        taskExecutionRoleArn: iam.TaskExecutionRoleArn,
        taskRoleArn: iam.TaskRoleArn,
        dbEndpoint: database.DbEndpoint,
        dbPort: database.DbPort,
        audioBucketName: storage.AudioBucketName,
        backendLogGroupName: monitoring.BackendLogGroupName,
        frontendLogGroupName: monitoring.FrontendLogGroupName,
        dbPasswordSecretArn: secrets.DbPasswordSecretArn,
        appSecretsArn: secrets.AppSecretsArn,
        apiKeysSecretArn: secrets.ApiKeysSecretArn,
        superuserPasswordSecretArn: secrets.SuperuserPasswordSecretArn);

    // ===================
    // Phase 5: DNS (Optional)
    // ===================

    var dns = new DnsStack("dns", config,
        albDnsName: compute.AlbDnsName,
        albZoneId: compute.AlbZoneId);

    // ===================
    // Stack Outputs
    // ===================

    return new Dictionary<string, object?>
    {
        // Networking
        ["vpcId"] = networking.VpcId,
        ["publicSubnetIds"] = networking.PublicSubnetIds,
        ["privateSubnetIds"] = networking.PrivateSubnetIds,

        // Database
        ["dbEndpoint"] = database.DbEndpoint,
        ["dbPort"] = database.DbPort,

        // Storage
        ["audioBucketName"] = storage.AudioBucketName,

        // ALB
        ["albDnsName"] = compute.AlbDnsName,

        // Application URLs
        ["frontendUrl"] = dns.DomainUrl,
        ["backendHealthUrl"] = Output.Format($"https://{apiDomain}/health"),

        // Secrets (ARNs only, not values)
        ["dbPasswordSecretArn"] = secrets.DbPasswordSecretArn,
        ["appSecretsArn"] = secrets.AppSecretsArn,
        ["apiKeysSecretArn"] = secrets.ApiKeysSecretArn,

        // IAM
        ["taskExecutionRoleArn"] = iam.TaskExecutionRoleArn,
        ["taskRoleArn"] = iam.TaskRoleArn,

        // Instructions
        ["nextSteps"] = config.HostedZoneId == null
            ? (apiDomain == config.DomainName
                ? Output.Format($"Create a DNS record pointing {config.DomainName} to {compute.AlbDnsName}")
                : Output.Format($"Create DNS records pointing {config.DomainName} and {apiDomain} to {compute.AlbDnsName}"))
            : Output.Create("DNS is configured automatically via Route53"),
    };
});
