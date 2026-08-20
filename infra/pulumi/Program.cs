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

    // Fails closed on an unusable or unintentionally open media configuration.
    config.ValidateMediaIngress();

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
    var monitoring = new MonitoringStack("monitoring", config,
        managedDbInstanceIdentifier: database.DbInstanceIdentifier);

    // IAM (roles and policies)
    var iam = new IamStack("iam", config,
        audioBucketArn: storage.AudioBucketArn,
        dbPasswordSecretArn: secrets.DbPasswordSecretArn,
        appSecretsArn: secrets.AppSecretsArn,
        apiKeysSecretArn: secrets.ApiKeysSecretArn,
        backendLogGroupArn: monitoring.BackendLogGroupArn,
        frontendLogGroupArn: monitoring.FrontendLogGroupArn,
        superuserPasswordSecretArn: secrets.SuperuserPasswordSecretArn);

    // ===================
    // Phase 4: Compute
    // ===================

    // Inbound UDP media path (optional): a Network Load Balancer with UDP
    // listeners in front of the same backend service, for providers that stream
    // live call audio as RTP. An ALB cannot carry UDP at all.
    MediaIngressStack? mediaIngress = null;
    if (config.MediaIngressEnabled)
    {
        mediaIngress = new MediaIngressStack("media-ingress", config,
            vpcId: networking.VpcId,
            publicSubnetIds: networking.PublicSubnetIds,
            securityGroupId: networking.MediaLoadBalancerSecurityGroupId!,
            healthCheckPort: 8000);
    }

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
        superuserPasswordSecretArn: secrets.SuperuserPasswordSecretArn,
        mediaAdvertiseAddress: mediaIngress?.AdvertiseAddress,
        mediaPorts: mediaIngress?.Ports,
        mediaTargetGroupArns: mediaIngress?.TargetGroupArns,
        // ECS rejects a target group whose load balancer has no listener yet,
        // so the service must wait for them.
        mediaListeners: mediaIngress?.Listeners);

    // ===================
    // Phase 5: DNS (Optional)
    // ===================

    var dns = new DnsStack("dns", config,
        albDnsName: compute.AlbDnsName,
        albZoneId: compute.AlbZoneId);

    // ===================
    // Phase 6: AWS Connect Lambda (Optional)
    // ===================

    LambdaStack? connectLambda = null;
    EventBridgeStack? eventBridge = null;

    if (config.DeployConnectLambda || config.DeployEventBridgeConsumer)
    {
        // Construct backend URL from API domain (shared by both Lambdas)
        var backendUrl = Output.Create($"https://{apiDomain}");

        if (config.DeployConnectLambda)
        {
            connectLambda = new LambdaStack("connect-lambda", config,
                backendUrl: backendUrl,
                connectApiKey: secrets.ConnectApiKey);
        }

        if (config.DeployEventBridgeConsumer)
        {
            eventBridge = new EventBridgeStack("eventbridge", config,
                backendUrl: backendUrl,
                connectApiKey: secrets.ConnectApiKey);
        }
    }

    // ===================
    // Stack Outputs
    // ===================

    var outputs = new Dictionary<string, object?>
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

        // AWS Connect Lambda (if deployed)
        ["connectLambdaArn"] = connectLambda?.FunctionArn,
        ["connectLambdaName"] = connectLambda?.FunctionName,

        // EventBridge Consumer (if deployed)
        ["eventBridgeLambdaArn"] = eventBridge?.FunctionArn,
        ["eventBridgeLambdaName"] = eventBridge?.FunctionName,
        ["eventBridgeRuleName"] = eventBridge?.RuleName,

        // Resource Naming
        ["resourcePrefix"] = config.EffectivePrefix,

        // Instructions
        ["nextSteps"] = config.HostedZoneId == null
            ? (apiDomain == config.DomainName
                ? Output.Format($"Create a DNS record pointing {config.DomainName} to {compute.AlbDnsName}")
                : Output.Format($"Create DNS records pointing {config.DomainName} and {apiDomain} to {compute.AlbDnsName}"))
            : Output.Create("DNS is configured automatically via Route53"),
    };

    // Added only when deployed, so a stack with media ingress off shows no
    // stack-output diff either — not just no resource diff.
    if (mediaIngress is not null)
    {
        // The advertise address is what the telephony provider sends RTP to, and
        // what it would allowlist.
        outputs["mediaAdvertiseAddress"] = mediaIngress.AdvertiseAddress;
        outputs["mediaDnsName"] = mediaIngress.DnsName;
    }

    return outputs;
});
