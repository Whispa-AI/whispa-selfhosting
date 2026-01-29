using Pulumi;

namespace Whispa.Aws.Pulumi.Configuration;

/// <summary>
/// Strongly-typed configuration for Whispa AWS deployment.
/// Values are read from Pulumi config (Pulumi.{stack}.yaml) or CLI.
/// </summary>
public class WhispaConfig
{
    private readonly global::Pulumi.Config _config;
    private readonly global::Pulumi.Config _awsConfig;

    public WhispaConfig()
    {
        _config = new global::Pulumi.Config("whispa");
        _awsConfig = new global::Pulumi.Config("aws");
    }

    // ===================
    // AWS Configuration
    // ===================

    /// <summary>AWS region for deployment (e.g., "us-east-1", "ap-southeast-2")</summary>
    public string AwsRegion => _awsConfig.Require("region");

    // ===================
    // Network Configuration
    // ===================

    /// <summary>VPC CIDR block (default: 10.0.0.0/16)</summary>
    public string VpcCidr => _config.Get("vpcCidr") ?? "10.0.0.0/16";

    // ===================
    // Domain & SSL
    // ===================

    /// <summary>Frontend domain name (e.g., "whispa.example.com")</summary>
    public string DomainName => _config.Require("domainName");

    /// <summary>ACM certificate ARN for HTTPS (must be in same region)</summary>
    public string? CertificateArn => _config.Get("certificateArn");

    /// <summary>Automatically request/validate ACM cert in Route53 (requires hostedZoneId)</summary>
    public bool AutoCertificate => _config.GetBoolean("autoCertificate") ?? false;

    /// <summary>Optional API domain name (e.g., "api.whispa.example.com")</summary>
    public string? ApiDomainName => _config.Get("apiDomainName");

    /// <summary>Route53 hosted zone ID (optional - for automatic DNS)</summary>
    public string? HostedZoneId => _config.Get("hostedZoneId");

    // ===================
    // Database Configuration
    // ===================

    /// <summary>RDS instance class (default: db.t3.medium)</summary>
    public string DbInstanceClass => _config.Get("dbInstanceClass") ?? "db.t3.medium";

    /// <summary>RDS allocated storage in GB (default: 20)</summary>
    public int DbAllocatedStorage => _config.GetInt32("dbAllocatedStorage") ?? 20;

    /// <summary>Database name (default: whispa)</summary>
    public string DbName => _config.Get("dbName") ?? "whispa";

    /// <summary>Database username (default: whispa_admin)</summary>
    public string DbUsername => _config.Get("dbUsername") ?? "whispa_admin";

    /// <summary>Enable Multi-AZ for RDS (default: false - enable for production)</summary>
    public bool DbMultiAz => _config.GetBoolean("dbMultiAz") ?? false;

    /// <summary>Backup retention period in days (default: 7)</summary>
    public int DbBackupRetentionDays => _config.GetInt32("dbBackupRetentionDays") ?? 7;

    // ===================
    // ECS Configuration
    // ===================

    /// <summary>Backend container image (default: ghcr.io/whispa/whispa-backend:latest)</summary>
    public string BackendImage => _config.Get("backendImage") ?? "ghcr.io/whispa/whispa-backend:latest";

    /// <summary>Frontend container image (default: ghcr.io/whispa/whispa-frontend:latest)</summary>
    public string FrontendImage => _config.Get("frontendImage") ?? "ghcr.io/whispa/whispa-frontend:latest";

    /// <summary>Backend CPU units (default: 512 = 0.5 vCPU)</summary>
    public int BackendCpu => _config.GetInt32("backendCpu") ?? 512;

    /// <summary>Backend memory in MB (default: 1024)</summary>
    public int BackendMemory => _config.GetInt32("backendMemory") ?? 1024;

    /// <summary>Frontend CPU units (default: 256 = 0.25 vCPU)</summary>
    public int FrontendCpu => _config.GetInt32("frontendCpu") ?? 256;

    /// <summary>Frontend memory in MB (default: 512)</summary>
    public int FrontendMemory => _config.GetInt32("frontendMemory") ?? 512;

    /// <summary>Number of task replicas (default: 1 for dev, use 2+ for production)</summary>
    public int DesiredCount => _config.GetInt32("desiredCount") ?? 1;

    // ===================
    // Email Configuration (AWS SES)
    // ===================

    /// <summary>Verified SES sender email address</summary>
    public string MailFrom => _config.Require("mailFrom");

    /// <summary>Sender display name (default: Whispa)</summary>
    public string MailFromName => _config.Get("mailFromName") ?? "Whispa";

    /// <summary>Feedback/support email address</summary>
    public string FeedbackEmail => _config.Get("feedbackEmail") ?? MailFrom;

    // ===================
    // Bootstrap Admin (Optional)
    // ===================

    /// <summary>Initial admin email (optional)</summary>
    public string? SuperuserEmail => _config.Get("superuserEmail");

    /// <summary>Initial admin password (optional, secret)</summary>
    public Output<string>? SuperuserPassword => _config.GetSecret("superuserPassword");

    /// <summary>Whether a bootstrap admin password is configured</summary>
    public bool HasSuperuserPassword => _config.Get("superuserPassword") != null;

    // ===================
    // Application URLs
    // ===================

    /// <summary>Frontend URL (e.g., "https://whispa.example.com")</summary>
    public string FrontendUrl => _config.Require("frontendUrl");

    /// <summary>CORS allowed origins as JSON array (e.g., '["https://whispa.example.com"]')</summary>
    public string CorsOrigins => _config.Get("corsOrigins") ?? $"[\"{FrontendUrl}\"]";

    // ===================
    // API Keys (Secrets)
    // ===================

    /// <summary>OpenRouter/LLM API key</summary>
    public Output<string> LlmApiKey => _config.RequireSecret("llmApiKey");

    /// <summary>LLM base URL (optional, defaults to OpenRouter)</summary>
    public string? LlmBaseUrl => _config.Get("llmBaseUrl");

    /// <summary>Deepgram API key (optional - for Deepgram STT)</summary>
    public Output<string>? DeepgramApiKey => _config.GetSecret("deepgramApiKey");

    /// <summary>Whether Deepgram API key is configured</summary>
    public bool HasDeepgramApiKey => _config.Get("deepgramApiKey") != null;

    /// <summary>ElevenLabs API key (optional - for ElevenLabs STT)</summary>
    public Output<string>? ElevenlabsApiKey => _config.GetSecret("elevenlabsApiKey");

    /// <summary>Whether ElevenLabs API key is configured</summary>
    public bool HasElevenlabsApiKey => _config.Get("elevenlabsApiKey") != null;

    // ===================
    // Transcription Settings
    // ===================

    /// <summary>Telephony transcription provider: elevenlabs, deepgram, or amazon (default: elevenlabs)</summary>
    public string TranscriptionProvider => _config.Get("transcriptionProvider") ?? "elevenlabs";

    // ===================
    // Feature Flags
    // ===================

    /// <summary>Enable scenario builder (default: true)</summary>
    public bool EnableScenarioBuilder => _config.GetBoolean("enableScenarioBuilder") ?? true;

    /// <summary>Enable prompt builder (default: true)</summary>
    public bool EnablePromptBuilder => _config.GetBoolean("enablePromptBuilder") ?? true;

    /// <summary>Enable training mode (default: false)</summary>
    public bool EnableTraining => _config.GetBoolean("enableTraining") ?? false;

    /// <summary>Show signup CTA (default: false for self-hosted)</summary>
    public bool ShowSignupCta => _config.GetBoolean("showSignupCta") ?? false;

    // ===================
    // Observability (Optional)
    // ===================

    /// <summary>Sentry DSN for error tracking (optional)</summary>
    public string? SentryDsn => _config.Get("sentryDsn");

    // ===================
    // AWS Connect Integration (Optional)
    // ===================

    /// <summary>Enable AWS Connect integration (default: false)</summary>
    public bool EnableAwsConnect => _config.GetBoolean("enableAwsConnect") ?? false;

    /// <summary>AWS Connect instance ID (required if enableAwsConnect is true)</summary>
    public string? ConnectInstanceId => _config.Get("connectInstanceId");

    /// <summary>KVS stream name prefix (default: whispa-connect)</summary>
    public string KvsStreamPrefix => _config.Get("kvsStreamPrefix") ?? "whispa-connect";

    /// <summary>API key for Connect Lambda to authenticate with Whispa backend (optional)</summary>
    public string? ConnectApiKey => _config.Get("connectApiKey");

    /// <summary>Deploy AWS Connect Lambda function via Pulumi (default: true when enableAwsConnect is true)</summary>
    public bool DeployConnectLambda => _config.GetBoolean("deployConnectLambda") ?? EnableAwsConnect;

    // ===================
    // Resource Naming
    // ===================

    /// <summary>
    /// Custom resource prefix (e.g., "whispa-dev", "acme-prod").
    /// If set, this is used directly instead of {projectName}-{environment}.
    /// </summary>
    public string? ResourcePrefix => _config.Get("resourcePrefix");

    /// <summary>Environment name used for resource naming (default: derived from stack name)</summary>
    public string Environment => _config.Get("environment") ?? Deployment.Instance.StackName;

    /// <summary>Project name prefix for resources (default: whispa)</summary>
    public string ProjectName => _config.Get("projectName") ?? "whispa";

    /// <summary>
    /// Generate a resource name with appropriate prefix.
    /// Uses resourcePrefix if set, otherwise {projectName}-{environment}-{name}.
    /// </summary>
    public string ResourceName(string name) =>
        !string.IsNullOrWhiteSpace(ResourcePrefix)
            ? $"{ResourcePrefix}-{name}"
            : $"{ProjectName}-{Environment}-{name}";

    /// <summary>
    /// Get the effective prefix used for resources (for display/documentation).
    /// </summary>
    public string EffectivePrefix =>
        !string.IsNullOrWhiteSpace(ResourcePrefix)
            ? ResourcePrefix
            : $"{ProjectName}-{Environment}";
}
