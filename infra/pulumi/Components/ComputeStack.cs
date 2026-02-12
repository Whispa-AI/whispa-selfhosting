using System;
using Pulumi;
using Pulumi.Aws.Ecs;
using Pulumi.Aws.Ecs.Inputs;
using Pulumi.Aws.Alb;
using Pulumi.Aws.Alb.Inputs;
using System.Collections.Immutable;
using Whispa.Aws.Pulumi.Configuration;
using System.Text.Json;

namespace Whispa.Aws.Pulumi.Components;

/// <summary>
/// Creates ECS Fargate compute infrastructure:
/// - Application Load Balancer with HTTPS
/// - ECS Cluster
/// - Task Definitions (backend, frontend)
/// - ECS Services
/// - Target Groups and Listeners
/// </summary>
public class ComputeStack : ComponentResource
{
    /// <summary>ALB DNS name</summary>
    public Output<string> AlbDnsName { get; }

    /// <summary>ALB hosted zone ID (for Route53 alias records)</summary>
    public Output<string> AlbZoneId { get; }

    /// <summary>Backend service URL (internal)</summary>
    public Output<string> BackendUrl { get; }

    /// <summary>Frontend URL (public)</summary>
    public Output<string> FrontendUrl { get; }

    public ComputeStack(
        string name,
        WhispaConfig config,
        Input<string> certificateArn,
        Output<string> vpcId,
        Output<ImmutableArray<string>> publicSubnetIds,
        Output<ImmutableArray<string>> privateSubnetIds,
        Output<string> albSecurityGroupId,
        Output<string> ecsSecurityGroupId,
        Output<string> taskExecutionRoleArn,
        Output<string> taskRoleArn,
        Output<string> dbEndpoint,
        Output<int> dbPort,
        Output<string> audioBucketName,
        Output<string> backendLogGroupName,
        Output<string> frontendLogGroupName,
        Output<string> dbPasswordSecretArn,
        Output<string> appSecretsArn,
        Output<string> apiKeysSecretArn,
        Output<string> superuserPasswordSecretArn,
        ComponentResourceOptions? options = null)
        : base("whispa:compute:ComputeStack", name, options)
    {
        var region = config.AwsRegion;

        // ===================
        // Application Load Balancer
        // ===================

        var alb = new LoadBalancer($"{name}-alb", new LoadBalancerArgs
        {
            Name = config.ResourceName("alb"),
            Internal = false,
            LoadBalancerType = "application",
            SecurityGroups = new[] { albSecurityGroupId },
            Subnets = publicSubnetIds,
            EnableDeletionProtection = config.Environment == "prod",
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("alb"),
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
            },
        }, new CustomResourceOptions { Parent = this });

        AlbDnsName = alb.DnsName;
        AlbZoneId = alb.ZoneId;

        // Backend target group
        // AWS Target Group names must be <= 32 chars and unique per region
        var backendTgName = TruncateTargetGroupName(config.ProjectName, config.Environment, "be");
        var backendTg = new TargetGroup($"{name}-backend-tg", new TargetGroupArgs
        {
            Name = backendTgName,
            Port = 8000,
            Protocol = "HTTP",
            VpcId = vpcId,
            TargetType = "ip",
            HealthCheck = new TargetGroupHealthCheckArgs
            {
                Path = "/health",
                Protocol = "HTTP",
                Port = "8000",
                HealthyThreshold = 2,
                UnhealthyThreshold = 3,
                Timeout = 5,
                Interval = 30,
                Matcher = "200",
            },
            DeregistrationDelay = 30,  // Faster deployments
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("backend-tg"),
            },
        }, new CustomResourceOptions { Parent = this });

        // Frontend target group
        var frontendTgName = TruncateTargetGroupName(config.ProjectName, config.Environment, "fe");
        var frontendTg = new TargetGroup($"{name}-frontend-tg", new TargetGroupArgs
        {
            Name = frontendTgName,
            Port = 3000,
            Protocol = "HTTP",
            VpcId = vpcId,
            TargetType = "ip",
            HealthCheck = new TargetGroupHealthCheckArgs
            {
                Path = "/",
                Protocol = "HTTP",
                Port = "3000",
                HealthyThreshold = 2,
                UnhealthyThreshold = 3,
                Timeout = 5,
                Interval = 30,
                Matcher = "200",
            },
            DeregistrationDelay = 30,
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("frontend-tg"),
            },
        }, new CustomResourceOptions { Parent = this });

        // HTTP listener - redirect to HTTPS
        new Listener($"{name}-http-listener", new ListenerArgs
        {
            LoadBalancerArn = alb.Arn,
            Port = 80,
            Protocol = "HTTP",
            DefaultActions = new[]
            {
                new ListenerDefaultActionArgs
                {
                    Type = "redirect",
                    Redirect = new ListenerDefaultActionRedirectArgs
                    {
                        Port = "443",
                        Protocol = "HTTPS",
                        StatusCode = "HTTP_301",
                    },
                },
            },
        }, new CustomResourceOptions { Parent = this });

        // HTTPS listener - route to frontend by default, backend via host/path rules
        var httpsListener = new Listener($"{name}-https-listener", new ListenerArgs
        {
            LoadBalancerArn = alb.Arn,
            Port = 443,
            Protocol = "HTTPS",
            SslPolicy = "ELBSecurityPolicy-TLS13-1-2-2021-06",
            CertificateArn = certificateArn,
            DefaultActions = new[]
            {
                new ListenerDefaultActionArgs
                {
                    Type = "forward",
                    TargetGroupArn = frontendTg.Arn,
                },
            },
        }, new CustomResourceOptions { Parent = this });

        // Route all requests for the API domain to backend
        if (!string.IsNullOrWhiteSpace(config.ApiDomainName) &&
            !string.Equals(config.ApiDomainName, config.DomainName, StringComparison.OrdinalIgnoreCase))
        {
            new ListenerRule($"{name}-backend-host-rule", new ListenerRuleArgs
            {
                ListenerArn = httpsListener.Arn,
                Priority = 5,
                Conditions = new[]
                {
                    new ListenerRuleConditionArgs
                    {
                        HostHeader = new ListenerRuleConditionHostHeaderArgs
                        {
                            Values = new[] { config.ApiDomainName },
                        },
                    },
                },
                Actions = new[]
                {
                    new ListenerRuleActionArgs
                    {
                        Type = "forward",
                        TargetGroupArn = backendTg.Arn,
                    },
                },
            }, new CustomResourceOptions { Parent = this });
        }

        // Route /api/* and other backend paths to backend
        var backendPaths = new[] { "/api/*", "/auth/*", "/health", "/docs", "/docs/*", "/openapi.json", "/redoc", "/ws/*" };
        for (int i = 0; i < backendPaths.Length; i++)
        {
            new ListenerRule($"{name}-backend-rule-{i}", new ListenerRuleArgs
            {
                ListenerArn = httpsListener.Arn,
                Priority = 10 + i,
                Conditions = new[]
                {
                    new ListenerRuleConditionArgs
                    {
                        PathPattern = new ListenerRuleConditionPathPatternArgs
                        {
                            Values = new[] { backendPaths[i] },
                        },
                    },
                },
                Actions = new[]
                {
                    new ListenerRuleActionArgs
                    {
                        Type = "forward",
                        TargetGroupArn = backendTg.Arn,
                    },
                },
            }, new CustomResourceOptions { Parent = this });
        }

        // ===================
        // ECS Cluster
        // ===================

        var cluster = new Cluster($"{name}-cluster", new ClusterArgs
        {
            Name = config.ResourceName("cluster"),
            Settings = new[]
            {
                new ClusterSettingArgs
                {
                    Name = "containerInsights",
                    Value = "enabled",
                },
            },
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("cluster"),
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
            },
        }, new CustomResourceOptions { Parent = this });

        // ===================
        // Backend Task Definition
        // ===================

        var backendContainerDef = Output.Tuple(
            dbEndpoint,
            dbPort,
            audioBucketName,
            backendLogGroupName,
            dbPasswordSecretArn,
            appSecretsArn,
            apiKeysSecretArn,
            superuserPasswordSecretArn
        ).Apply(values =>
        {
            var dbHost = values.Item1;
            var dbPortValue = values.Item2;
            var bucket = values.Item3;
            var logGroup = values.Item4;
            var dbSecretArn = values.Item5;
            var appSecretArn = values.Item6;
            var apiSecretArn = values.Item7;
            var superuserSecretArn = values.Item8;

            return JsonSerializer.Serialize(new[]
            {
                new
                {
                    name = "backend",
                    image = config.BackendImage,
                    essential = true,
                    portMappings = new[]
                    {
                        new { containerPort = 8000, protocol = "tcp" },
                    },
                    environment = new[]
                    {
                        // Database
                        new { name = "DB_HOST", value = dbHost },
                        new { name = "DB_PORT", value = dbPortValue.ToString() },
                        new { name = "DB_NAME", value = config.DbName },
                        new { name = "DB_USER", value = config.DbUsername },

                        // Email
                        new { name = "AWS_SES_REGION", value = region },
                        new { name = "MAIL_FROM", value = config.MailFrom },
                        new { name = "MAIL_FROM_NAME", value = config.MailFromName },
                        new { name = "FEEDBACK_EMAIL", value = config.FeedbackEmail },

                        // URLs
                        new { name = "FRONTEND_URL", value = config.FrontendUrl },
                        new { name = "CORS_ORIGINS", value = config.CorsOrigins },

                        // Bootstrap superuser
                        new { name = "SUPERUSER_EMAIL", value = config.SuperuserEmail },

                        // S3
                        new { name = "S3_AUDIO_BUCKET", value = bucket },
                        new { name = "S3_AUDIO_REGION", value = region },

                        // Transcription
                        new { name = "TELEPHONY_TRANSCRIPTION_PROVIDER", value = config.TranscriptionProvider },

                        // Feature flags
                        new { name = "SHOW_SIGNUP_CTA", value = config.ShowSignupCta.ToString().ToLower() },

                        // Observability
                        new { name = "SENTRY_ENVIRONMENT", value = config.Environment },
                        new { name = "SENTRY_DSN", value = config.SentryDsn ?? "" },
                        new { name = "LANGFUSE_PUBLIC_KEY", value = config.LangfusePublicKey ?? "" },
                        new { name = "LANGFUSE_HOST", value = config.LangfuseHost ?? "" },

                        // LLM base URL (if custom)
                        new { name = "LLM_BASE_URL", value = config.LlmBaseUrl ?? "" },

                        // LLM model configuration
                        new { name = "LLM_MODEL_DEFAULT", value = config.LlmModelDefault ?? "" },
                        new { name = "LLM_MODEL_ACTION_CARDS", value = config.LlmModelActionCards ?? "" },
                        new { name = "LLM_MODEL_WORKFLOW", value = config.LlmModelWorkflow ?? "" },
                        new { name = "LLM_MODEL_SUGGESTED_RESPONSES", value = config.LlmModelSuggestedResponses ?? "" },
                        new { name = "LLM_MODEL_SENTIMENT", value = config.LlmModelSentiment ?? "" },
                        new { name = "LLM_MODEL_COACHING", value = config.LlmModelCoaching ?? "" },
                        new { name = "LLM_MODEL_SUMMARY", value = config.LlmModelSummary ?? "" },
                        new { name = "LLM_MODEL_CLASSIFICATION", value = config.LlmModelClassification ?? "" },

                        // AWS Bedrock (region for bedrock/* model prefixes)
                        new { name = "AWS_BEDROCK_REGION", value = config.BedrockRegion ?? "" },

                        // AWS service regions (default to deployment region)
                        new { name = "AWS_TRANSCRIBE_REGION", value = config.AwsRegion },
                        new { name = "AWS_CONNECT_REGION", value = config.AwsRegion },
                    },
                    secrets = BuildSecretsList(
                        appSecretArn,
                        apiSecretArn,
                        dbSecretArn,
                        superuserSecretArn,
                        config.HasLlmApiKey,
                        config.HasDeepgramApiKey,
                        config.HasElevenlabsApiKey,
                        config.HasLangfuseSecretKey,
                        config.EnableAwsConnect
                    ),
                    logConfiguration = new
                    {
                        logDriver = "awslogs",
                        options = new Dictionary<string, string>
                        {
                            ["awslogs-group"] = logGroup,
                            ["awslogs-region"] = region,
                            ["awslogs-stream-prefix"] = "ecs",
                        },
                    },
                    healthCheck = new
                    {
                        command = new[] { "CMD-SHELL", "curl -f http://localhost:8000/health || exit 1" },
                        interval = 30,
                        timeout = 5,
                        retries = 3,
                        startPeriod = 60,
                    },
                },
            });
        });

        var backendTaskDef = new TaskDefinition($"{name}-backend-task", new TaskDefinitionArgs
        {
            Family = config.ResourceName("backend"),
            RequiresCompatibilities = new[] { "FARGATE" },
            NetworkMode = "awsvpc",
            Cpu = config.BackendCpu.ToString(),
            Memory = config.BackendMemory.ToString(),
            ExecutionRoleArn = taskExecutionRoleArn,
            TaskRoleArn = taskRoleArn,
            ContainerDefinitions = backendContainerDef,
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("backend-task"),
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
            },
        }, new CustomResourceOptions { Parent = this });

        // ===================
        // Frontend Task Definition
        // ===================

        var frontendContainerDef = frontendLogGroupName.Apply(logGroup =>
        {
            return JsonSerializer.Serialize(new[]
            {
                new
                {
                    name = "frontend",
                    image = config.FrontendImage,
                    essential = true,
                    portMappings = new[]
                    {
                        new { containerPort = 3000, protocol = "tcp" },
                    },
                    environment = new[]
                    {
                        // Next.js runtime config - API calls go through ALB
                        new { name = "NEXT_PUBLIC_API_BASE_URL", value = $"https://{(string.IsNullOrWhiteSpace(config.ApiDomainName) ? config.DomainName : config.ApiDomainName)}" },
                        // Version display - extract tag from image, ignore "latest"
                        new { name = "NEXT_PUBLIC_APP_VERSION", value = GetVersionFromImage(config.FrontendImage) },
                    },
                    logConfiguration = new
                    {
                        logDriver = "awslogs",
                        options = new Dictionary<string, string>
                        {
                            ["awslogs-group"] = logGroup,
                            ["awslogs-region"] = region,
                            ["awslogs-stream-prefix"] = "ecs",
                        },
                    },
                },
            });
        });

        var frontendTaskDef = new TaskDefinition($"{name}-frontend-task", new TaskDefinitionArgs
        {
            Family = config.ResourceName("frontend"),
            RequiresCompatibilities = new[] { "FARGATE" },
            NetworkMode = "awsvpc",
            Cpu = config.FrontendCpu.ToString(),
            Memory = config.FrontendMemory.ToString(),
            ExecutionRoleArn = taskExecutionRoleArn,
            TaskRoleArn = taskRoleArn,
            ContainerDefinitions = frontendContainerDef,
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("frontend-task"),
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
            },
        }, new CustomResourceOptions { Parent = this });

        // ===================
        // ECS Services
        // ===================

        var backendService = new Service($"{name}-backend-service", new ServiceArgs
        {
            Name = config.ResourceName("backend"),
            Cluster = cluster.Arn,
            TaskDefinition = backendTaskDef.Arn,
            DesiredCount = config.DesiredCount,
            LaunchType = "FARGATE",
            NetworkConfiguration = new ServiceNetworkConfigurationArgs
            {
                Subnets = privateSubnetIds,
                SecurityGroups = new[] { ecsSecurityGroupId },
                AssignPublicIp = false,
            },
            LoadBalancers = new[]
            {
                new ServiceLoadBalancerArgs
                {
                    TargetGroupArn = backendTg.Arn,
                    ContainerName = "backend",
                    ContainerPort = 8000,
                },
            },
            DeploymentCircuitBreaker = new ServiceDeploymentCircuitBreakerArgs
            {
                Enable = true,
                Rollback = true,
            },
            EnableExecuteCommand = true,  // For debugging with ECS Exec
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("backend-service"),
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
            },
        }, new CustomResourceOptions { Parent = this, DependsOn = { httpsListener } });

        var frontendService = new Service($"{name}-frontend-service", new ServiceArgs
        {
            Name = config.ResourceName("frontend"),
            Cluster = cluster.Arn,
            TaskDefinition = frontendTaskDef.Arn,
            DesiredCount = config.DesiredCount,
            LaunchType = "FARGATE",
            NetworkConfiguration = new ServiceNetworkConfigurationArgs
            {
                Subnets = privateSubnetIds,
                SecurityGroups = new[] { ecsSecurityGroupId },
                AssignPublicIp = false,
            },
            LoadBalancers = new[]
            {
                new ServiceLoadBalancerArgs
                {
                    TargetGroupArn = frontendTg.Arn,
                    ContainerName = "frontend",
                    ContainerPort = 3000,
                },
            },
            DeploymentCircuitBreaker = new ServiceDeploymentCircuitBreakerArgs
            {
                Enable = true,
                Rollback = true,
            },
            EnableExecuteCommand = true,
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("frontend-service"),
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
            },
        }, new CustomResourceOptions { Parent = this, DependsOn = { httpsListener } });

        // Backend is accessed via ALB path-based routing, not directly on port 8000
        var apiDomain = string.IsNullOrWhiteSpace(config.ApiDomainName) ? config.DomainName : config.ApiDomainName;
        BackendUrl = Output.Format($"https://{apiDomain}");
        FrontendUrl = Output.Format($"https://{config.DomainName}");

        RegisterOutputs();
    }

    /// <summary>
    /// Creates a deterministic target group name that fits within AWS's 32-char limit.
    /// Uses a hash suffix if the full name would be too long.
    /// </summary>
    private static string TruncateTargetGroupName(string project, string environment, string suffix)
    {
        var fullName = $"{project}-{environment}-{suffix}";
        if (fullName.Length <= 32)
            return fullName;

        // If too long, truncate and add a short hash for uniqueness
        var hash = Math.Abs($"{project}{environment}".GetHashCode()).ToString("x8").Substring(0, 6);
        var maxPrefixLen = 32 - suffix.Length - hash.Length - 2; // 2 for the dashes
        var prefix = $"{project}-{environment}".Substring(0, Math.Min(maxPrefixLen, $"{project}-{environment}".Length));
        return $"{prefix}-{hash}-{suffix}";
    }

    /// <summary>
    /// Builds the list of secrets to inject into the backend container.
    /// Only includes optional API keys (Deepgram, ElevenLabs) if they are configured.
    /// </summary>
    private static object[] BuildSecretsList(
        string appSecretArn,
        string apiSecretArn,
        string dbSecretArn,
        string superuserPasswordSecretArn,
        bool hasLlmApiKey,
        bool hasDeepgram,
        bool hasElevenlabs,
        bool hasLangfuseSecretKey,
        bool enableAwsConnect
    )
    {
        var secrets = new List<object>
        {
            // JWT keys from app secrets (always required)
            new { name = "ACCESS_SECRET_KEY", valueFrom = $"{appSecretArn}:ACCESS_SECRET_KEY::" },
            new { name = "RESET_PASSWORD_SECRET_KEY", valueFrom = $"{appSecretArn}:RESET_PASSWORD_SECRET_KEY::" },
            new { name = "VERIFICATION_SECRET_KEY", valueFrom = $"{appSecretArn}:VERIFICATION_SECRET_KEY::" },
            new { name = "REFRESH_SECRET_KEY", valueFrom = $"{appSecretArn}:REFRESH_SECRET_KEY::" },

            // Database password (stored as plain secret string)
            new { name = "DB_PASSWORD", valueFrom = dbSecretArn },

            // Bootstrap superuser password
            new { name = "SUPERUSER_PASSWORD", valueFrom = superuserPasswordSecretArn },
        };

        // LLM API key (optional when using Bedrock)
        if (hasLlmApiKey)
        {
            secrets.Add(new { name = "LLM_API_KEY", valueFrom = $"{apiSecretArn}:LLM_API_KEY::" });
        }

        // Only include optional transcription provider keys if configured
        if (hasDeepgram)
        {
            secrets.Add(new { name = "DEEPGRAM_API_KEY", valueFrom = $"{apiSecretArn}:DEEPGRAM_API_KEY::" });
        }

        if (hasElevenlabs)
        {
            secrets.Add(new { name = "ELEVENLABS_API_KEY", valueFrom = $"{apiSecretArn}:ELEVENLABS_API_KEY::" });
        }

        if (hasLangfuseSecretKey)
        {
            secrets.Add(new { name = "LANGFUSE_SECRET_KEY", valueFrom = $"{apiSecretArn}:LANGFUSE_SECRET_KEY::" });
        }

        // Connect API key for Lambda-to-backend authentication
        if (enableAwsConnect)
        {
            secrets.Add(new { name = "CONNECT_API_KEY", valueFrom = $"{appSecretArn}:CONNECT_API_KEY::" });
        }

        return secrets.ToArray();
    }

    /// <summary>
    /// Extracts a meaningful version from a container image tag.
    /// Returns empty string for "latest" or untagged images.
    /// </summary>
    private static string GetVersionFromImage(string image)
    {
        if (!image.Contains(':'))
            return "";
        var tag = image.Split(':').Last();
        return string.Equals(tag, "latest", StringComparison.OrdinalIgnoreCase) ? "" : tag;
    }
}
