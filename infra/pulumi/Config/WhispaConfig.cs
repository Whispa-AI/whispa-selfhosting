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

    /// <summary>Additional externally-managed RDS instance identifiers to alarm on (optional)</summary>
    public string[] RdsInstanceIdentifiers => _config.GetObject<string[]>("rdsInstanceIdentifiers") ?? [];

    /// <summary>RDS cluster identifiers to alarm on (optional, e.g. Aurora)</summary>
    public string[] RdsClusterIdentifiers => _config.GetObject<string[]>("rdsClusterIdentifiers") ?? [];

    // ===================
    // ECS Configuration
    // ===================

    /// <summary>Container registry + org prefix for app images (default: ghcr.io/whispa-ai)</summary>
    public string ImageRegistry => _config.Get("imageRegistry") ?? "ghcr.io/whispa-ai";

    /// <summary>
    /// Backend container image. Precedence: explicit whispa:backendImage full ref >
    /// whispa:imageTag > the version baked into this infra release
    /// (<see cref="BuildInfo.DefaultImageTag"/>). Resolution lives in <see cref="ImageRef"/>.
    /// </summary>
    public string BackendImage => ImageRef.Resolve(
        _config.Get("backendImage"), _config.Get("imageTag"),
        ImageRegistry, "whispa-backend", BuildInfo.DefaultImageTag);

    /// <summary>Frontend container image. Same precedence as <see cref="BackendImage"/>.</summary>
    public string FrontendImage => ImageRef.Resolve(
        _config.Get("frontendImage"), _config.Get("imageTag"),
        ImageRegistry, "whispa-frontend", BuildInfo.DefaultImageTag);

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

    /// <summary>Client/deployment name stamped into feedback email subjects (default: Whispa)</summary>
    public string ClientName => _config.Get("clientName") ?? "Whispa";

    // ===================
    // Bootstrap Admin
    // ===================

    /// <summary>Initial admin email (required)</summary>
    public string SuperuserEmail => _config.Require("superuserEmail");

    /// <summary>Initial admin password (required, secret)</summary>
    public Output<string> SuperuserPassword => _config.RequireSecret("superuserPassword");

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

    /// <summary>OpenRouter/LLM API key (optional when using Bedrock models)</summary>
    public Output<string>? LlmApiKey => _config.GetSecret("llmApiKey");

    /// <summary>Whether an LLM API key is configured</summary>
    public bool HasLlmApiKey => _config.Get("llmApiKey") != null;

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

    /// <summary>AssemblyAI API key (optional - for AssemblyAI STT)</summary>
    public Output<string>? AssemblyaiApiKey => _config.GetSecret("assemblyaiApiKey");

    /// <summary>Whether AssemblyAI API key is configured</summary>
    public bool HasAssemblyaiApiKey => _config.Get("assemblyaiApiKey") != null;

    // ===================
    // Generic env var passthrough (experimental / fast-moving config)
    // ===================

    /// <summary>
    /// Plaintext env passthrough: a map of NAME =&gt; value injected verbatim into the backend
    /// container's environment. Use for non-secret, experimental, or fast-moving config without
    /// changing infra code — set it in the stack yaml as `whispa:extraEnv`. NOTE: values are
    /// visible in the task definition and logs; put API keys / secrets in ExtraSecrets instead.
    /// </summary>
    public Dictionary<string, string> ExtraEnv =>
        _config.GetObject<Dictionary<string, string>>("extraEnv") ?? new Dictionary<string, string>();

    /// <summary>
    /// Secret env passthrough: a map of ENV_VAR_NAME =&gt; secret value. Values are stored in the
    /// api-keys Secrets Manager secret and injected via the container's `secrets` block (never in
    /// plaintext). Set per key with:
    ///   pulumi config set --secret --path 'extraSecrets.CARTESIA_API_KEY' YOUR_KEY
    /// </summary>
    public Output<Dictionary<string, string>> ExtraSecrets =>
        _config.GetSecretObject<Dictionary<string, string>>("extraSecrets")
            ?? Output.Create(new Dictionary<string, string>());

    // ===================
    // LLM Model Configuration
    // ===================

    /// <summary>Default LLM model for all analyzers</summary>
    public string? LlmModelDefault => _config.Get("llmModelDefault");

    /// <summary>LLM model for action cards analyzer</summary>
    public string? LlmModelActionCards => _config.Get("llmModelActionCards");

    /// <summary>LLM model for workflow progress analyzer</summary>
    public string? LlmModelWorkflow => _config.Get("llmModelWorkflow");

    /// <summary>LLM model for suggested responses analyzer</summary>
    public string? LlmModelSuggestedResponses => _config.Get("llmModelSuggestedResponses");

    /// <summary>LLM model for sentiment analyzer</summary>
    public string? LlmModelSentiment => _config.Get("llmModelSentiment");

    /// <summary>LLM model for coaching feedback</summary>
    public string? LlmModelCoaching => _config.Get("llmModelCoaching");

    /// <summary>LLM model for summary generation</summary>
    public string? LlmModelSummary => _config.Get("llmModelSummary");

    /// <summary>LLM model for call classification</summary>
    public string? LlmModelClassification => _config.Get("llmModelClassification");

    /// <summary>LLM model for QA scorecard generation</summary>
    public string? LlmModelScorecard => _config.Get("llmModelScorecard");

    // ===================
    // AWS Bedrock Configuration
    // ===================

    /// <summary>
    /// Default LLM provider passed to the backend (bedrock | openrouter | openai).
    /// "bedrock" (default) needs no API key and works with zero model config —
    /// the backend derives a region-appropriate Claude default. Set to a
    /// non-Bedrock provider only if you supply llmApiKey + model overrides.
    /// </summary>
    public string LlmProvider => _config.Get("llmProvider") ?? "bedrock";

    /// <summary>Whether this deployment uses AWS Bedrock for LLM calls (drives IAM grants).</summary>
    public bool UseBedrock => string.Equals(LlmProvider, "bedrock", StringComparison.OrdinalIgnoreCase);

    /// <summary>AWS region for Bedrock API calls (defaults to the deployment region).</summary>
    public string BedrockRegion => _config.Get("bedrockRegion") ?? AwsRegion;

    // ===================
    // Transcription Settings
    // ===================

    /// <summary>Telephony transcription provider: elevenlabs, deepgram, assemblyai, or amazon (default: elevenlabs)</summary>
    public string TranscriptionProvider => _config.Get("transcriptionProvider") ?? "elevenlabs";

    /// <summary>
    /// AssemblyAI streaming base URL (scheme+host[:port], no path). Point at a self-hosted
    /// Universal-Streaming deployment for data residency; leave unset to use AssemblyAI's
    /// managed cloud. Both streaming paths (agent-assist WebSocket + AI-call plugin) append "/v3/ws".
    /// </summary>
    public string? AssemblyaiStreamingBaseUrl => _config.Get("assemblyaiStreamingBaseUrl");

    /// <summary>
    /// Master switch for AssemblyAI dynamic keyterm biasing (STT_DYNAMIC_KEYTERMS_ENABLED).
    /// Off by default — keyterm prompting is billed separately, so it's opt-in per deployment.
    /// When on, the orchestrator pushes a scenario's seeded stt_keyterms after the first turn.
    /// </summary>
    public bool SttDynamicKeyterms => _config.GetBoolean("sttDynamicKeyterms") ?? false;

    /// <summary>
    /// Adds the built-in AU collections vocabulary to the keyterm set (STT_KEYTERM_DOMAIN_DEFAULTS).
    /// Only has effect when sttDynamicKeyterms is enabled.
    /// </summary>
    public bool SttKeytermDomainDefaults => _config.GetBoolean("sttKeytermDomainDefaults") ?? false;

    // ===================
    // AI Voice Calls (LiveKit)
    // ===================

    /// <summary>LiveKit project URL (wss://...). When unset, the in-process AI-call worker stays dormant.</summary>
    public string? LiveKitUrl => _config.Get("liveKitUrl");

    /// <summary>LiveKit API key (secret).</summary>
    public Output<string>? LiveKitApiKey => _config.GetSecret("liveKitApiKey");

    /// <summary>LiveKit API secret (secret).</summary>
    public Output<string>? LiveKitApiSecret => _config.GetSecret("liveKitApiSecret");

    /// <summary>Whether LiveKit credentials are configured (gates secret injection + the AI-call worker).</summary>
    public bool HasLiveKit => _config.Get("liveKitApiKey") != null && _config.Get("liveKitApiSecret") != null;

    /// <summary>LiveKit outbound SIP trunk ID (ST_...) for dialing out via Twilio. Optional.</summary>
    public string? LiveKitSipOutboundTrunkId => _config.Get("liveKitSipOutboundTrunkId");

    /// <summary>
    /// Worker name the AI-call worker registers under. MUST be unique per environment: workers
    /// sharing a name against one LiveKit project form a single pool and jobs round-robin across
    /// them. Defaults to "whispa-ai-collector-{environment}" so stacks never collide by accident.
    /// </summary>
    public string? LiveKitAiAgentName => _config.Get("liveKitAiAgentName");

    // ===================
    // Seeding
    // ===================

    /// <summary>
    /// Comma-separated scenario names to auto-seed on container startup (after migrations).
    /// Empty/null = no auto-seeding. "all" = seed every scenario. "a,b,c" = seed those filters.
    /// Maps to the SEED_SCENARIOS env var consumed by entrypoint.prod.sh.
    /// </summary>
    public string? SeedScenarios => _config.Get("seedScenarios");

    // ===================
    // Feature Flags
    // ===================

    /// <summary>Show signup CTA (default: false for self-hosted)</summary>
    public bool ShowSignupCta => _config.GetBoolean("showSignupCta") ?? false;

    // ===================
    // Observability (Optional)
    // ===================

    /// <summary>Sentry DSN for error tracking (optional)</summary>
    public string? SentryDsn => _config.Get("sentryDsn");

    /// <summary>Langfuse public key (optional)</summary>
    public string? LangfusePublicKey => _config.Get("langfusePublicKey");

    /// <summary>Langfuse secret key (optional)</summary>
    public Output<string>? LangfuseSecretKey => _config.GetSecret("langfuseSecretKey");

    /// <summary>Whether Langfuse secret key is configured</summary>
    public bool HasLangfuseSecretKey => _config.Get("langfuseSecretKey") != null;

    /// <summary>Langfuse host URL (optional, for self-hosted Langfuse)</summary>
    public string? LangfuseHost => _config.Get("langfuseHost");

    /// <summary>Whether to create RDS I/O alarms (default: enabled when SNS/email config is provided)</summary>
    public bool EnableRdsIoAlarms =>
        _config.GetBoolean("enableRdsIoAlarms")
        ?? (!string.IsNullOrWhiteSpace(AlarmSnsTopicArn)
            || !string.IsNullOrWhiteSpace(AlarmEmailAddress));

    /// <summary>Existing SNS topic ARN for CloudWatch alarm notifications (optional)</summary>
    public string? AlarmSnsTopicArn => _config.Get("alarmSnsTopicArn");

    /// <summary>Email address to subscribe to the RDS alarm SNS topic (optional)</summary>
    public string? AlarmEmailAddress => _config.Get("alarmEmailAddress");

    /// <summary>DiskQueueDepth alarm threshold (default: 10)</summary>
    public double RdsDiskQueueDepthAlarmThreshold => _config.GetDouble("rdsDiskQueueDepthAlarmThreshold") ?? 10;

    /// <summary>ReadIOPS alarm threshold for RDS instances (default: 1000)</summary>
    public double RdsReadIopsAlarmThreshold => _config.GetDouble("rdsReadIopsAlarmThreshold") ?? 1000;

    /// <summary>WriteIOPS alarm threshold for RDS instances (default: 1000)</summary>
    public double RdsWriteIopsAlarmThreshold => _config.GetDouble("rdsWriteIopsAlarmThreshold") ?? 1000;

    /// <summary>VolumeReadIOPs alarm threshold for Aurora clusters (default: 1000)</summary>
    public double RdsVolumeReadIopsAlarmThreshold => _config.GetDouble("rdsVolumeReadIopsAlarmThreshold") ?? 1000;

    /// <summary>VolumeWriteIOPS alarm threshold for Aurora clusters (default: 1000)</summary>
    public double RdsVolumeWriteIopsAlarmThreshold => _config.GetDouble("rdsVolumeWriteIopsAlarmThreshold") ?? 1000;

    /// <summary>Free storage space threshold in bytes for standard RDS instances (default: 5 GiB)</summary>
    public double RdsFreeStorageSpaceAlarmThreshold => _config.GetDouble("rdsFreeStorageSpaceAlarmThreshold")
        ?? 5d * 1024 * 1024 * 1024;

    /// <summary>Freeable memory threshold in bytes for RDS instances (default: 256 MiB)</summary>
    public double RdsFreeableMemoryAlarmThreshold => _config.GetDouble("rdsFreeableMemoryAlarmThreshold")
        ?? 256d * 1024 * 1024;

    /// <summary>CPU utilization percentage threshold for RDS instances (default: 80)</summary>
    public double RdsCpuUtilizationAlarmThreshold => _config.GetDouble("rdsCpuUtilizationAlarmThreshold") ?? 80;

    /// <summary>Free local storage threshold in bytes for Aurora clusters (default: 2 GiB)</summary>
    public double RdsFreeLocalStorageAlarmThreshold => _config.GetDouble("rdsFreeLocalStorageAlarmThreshold")
        ?? 2d * 1024 * 1024 * 1024;

    /// <summary>Aurora volume bytes left threshold in bytes for Aurora MySQL clusters (default: 10 GiB)</summary>
    public double AuroraVolumeBytesLeftTotalAlarmThreshold => _config.GetDouble("auroraVolumeBytesLeftTotalAlarmThreshold")
        ?? 10d * 1024 * 1024 * 1024;

    /// <summary>Aurora MySQL cluster identifiers for AuroraVolumeBytesLeftTotal alarms (optional)</summary>
    public string[] AuroraMySqlClusterIdentifiers => _config.GetObject<string[]>("auroraMySqlClusterIdentifiers") ?? [];

    // ===================
    // AWS Connect Integration (Optional)
    // ===================

    /// <summary>Enable AWS Connect integration (auto-enabled if connectInstanceId is set)</summary>
    public bool EnableAwsConnect => _config.GetBoolean("enableAwsConnect") ?? !string.IsNullOrWhiteSpace(ConnectInstanceId);

    /// <summary>AWS Connect instance ID (required if enableAwsConnect is true)</summary>
    public string? ConnectInstanceId => _config.Get("connectInstanceId");

    /// <summary>KVS stream name prefix (default: whispa-connect)</summary>
    public string KvsStreamPrefix => _config.Get("kvsStreamPrefix") ?? "whispa-connect";

    // Note: Connect API key is now auto-generated in SecretsStack
    // and passed directly to the Lambda and backend container.

    /// <summary>Deploy AWS Connect Lambda function via Pulumi (default: true when enableAwsConnect is true)</summary>
    public bool DeployConnectLambda => _config.GetBoolean("deployConnectLambda") ?? EnableAwsConnect;

    /// <summary>Deploy EventBridge consumer Lambda for Connect contact events (default: true when enableAwsConnect is true)</summary>
    public bool DeployEventBridgeConsumer => _config.GetBoolean("deployEventBridgeConsumer") ?? EnableAwsConnect;

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
