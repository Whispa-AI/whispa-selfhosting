using Pulumi;
using Pulumi.Aws.SecretsManager;
using Pulumi.Aws.SecretsManager.Inputs;
using Pulumi.Random;
using Whispa.Aws.Pulumi.Configuration;

namespace Whispa.Aws.Pulumi.Components;

/// <summary>
/// Creates and manages secrets in AWS Secrets Manager:
/// - Database password (auto-generated)
/// - JWT authentication keys (auto-generated)
/// - API keys (from config)
///
/// These secrets are referenced by ECS task definitions for secure injection.
/// </summary>
public class SecretsStack : ComponentResource
{
    /// <summary>Database password secret ARN</summary>
    public Output<string> DbPasswordSecretArn { get; }

    /// <summary>Database password value (for constructing connection string)</summary>
    public Output<string> DbPassword { get; }

    /// <summary>Application secrets ARN (JWT keys)</summary>
    public Output<string> AppSecretsArn { get; }

    /// <summary>API keys secret ARN (LLM, Deepgram, ElevenLabs)</summary>
    public Output<string> ApiKeysSecretArn { get; }

    /// <summary>Connect API key value (for Lambda env var)</summary>
    public Output<string> ConnectApiKey { get; }

    /// <summary>Bootstrap superuser password secret ARN (optional)</summary>
    public Output<string> SuperuserPasswordSecretArn { get; }

    public SecretsStack(string name, WhispaConfig config, ComponentResourceOptions? options = null)
        : base("whispa:secrets:SecretsStack", name, options)
    {
        // ===================
        // Generate Secure Random Values
        // ===================

        // Database password - 32 chars, alphanumeric only (no special chars that might break connection strings)
        var dbPassword = new RandomPassword($"{name}-db-password", new RandomPasswordArgs
        {
            Length = 32,
            Special = false,
        }, new CustomResourceOptions { Parent = this });

        DbPassword = dbPassword.Result;

        // JWT secret keys - 64 chars for strong security
        var accessSecretKey = new RandomPassword($"{name}-access-key", new RandomPasswordArgs
        {
            Length = 64,
            Special = false,
        }, new CustomResourceOptions { Parent = this });

        var resetPasswordSecretKey = new RandomPassword($"{name}-reset-key", new RandomPasswordArgs
        {
            Length = 64,
            Special = false,
        }, new CustomResourceOptions { Parent = this });

        var verificationSecretKey = new RandomPassword($"{name}-verification-key", new RandomPasswordArgs
        {
            Length = 64,
            Special = false,
        }, new CustomResourceOptions { Parent = this });

        var refreshSecretKey = new RandomPassword($"{name}-refresh-key", new RandomPasswordArgs
        {
            Length = 64,
            Special = false,
        }, new CustomResourceOptions { Parent = this });

        // Connect API key - auto-generated for Lambda-to-backend auth
        var connectApiKey = new RandomPassword($"{name}-connect-api-key", new RandomPasswordArgs
        {
            Length = 48,
            Special = false,
        }, new CustomResourceOptions { Parent = this });

        ConnectApiKey = connectApiKey.Result;

        // ===================
        // Database Password Secret
        // ===================

        var dbPasswordSecret = new Secret($"{name}-db-password", new SecretArgs
        {
            Name = $"{config.ProjectName}/{config.Environment}/database-password",
            Description = "RDS PostgreSQL master password",
            Tags = new InputMap<string>
            {
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
            },
        }, new CustomResourceOptions { Parent = this });

        new SecretVersion($"{name}-db-password-version", new SecretVersionArgs
        {
            SecretId = dbPasswordSecret.Id,
            SecretString = dbPassword.Result,
        }, new CustomResourceOptions { Parent = this });

        DbPasswordSecretArn = dbPasswordSecret.Arn;

        // ===================
        // Application Secrets (JWT Keys)
        // ===================

        var appSecrets = new Secret($"{name}-app-secrets", new SecretArgs
        {
            Name = $"{config.ProjectName}/{config.Environment}/app-secrets",
            Description = "Application secrets (JWT keys)",
            Tags = new InputMap<string>
            {
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
            },
        }, new CustomResourceOptions { Parent = this });

        // Store JWT keys + Connect API key as JSON
        var appSecretsJson = Output.All(
            accessSecretKey.Result,
            resetPasswordSecretKey.Result,
            verificationSecretKey.Result,
            refreshSecretKey.Result,
            connectApiKey.Result
        ).Apply(keys => System.Text.Json.JsonSerializer.Serialize(new
        {
            ACCESS_SECRET_KEY = keys[0],
            RESET_PASSWORD_SECRET_KEY = keys[1],
            VERIFICATION_SECRET_KEY = keys[2],
            REFRESH_SECRET_KEY = keys[3],
            CONNECT_API_KEY = keys[4],
        }));

        new SecretVersion($"{name}-app-secrets-version", new SecretVersionArgs
        {
            SecretId = appSecrets.Id,
            SecretString = appSecretsJson,
        }, new CustomResourceOptions { Parent = this });

        AppSecretsArn = appSecrets.Arn;

        // ===================
        // API Keys Secret (from user config)
        // ===================

        var apiKeysSecret = new Secret($"{name}-api-keys", new SecretArgs
        {
            Name = $"{config.ProjectName}/{config.Environment}/api-keys",
            Description = "External API keys (LLM, transcription providers)",
            Tags = new InputMap<string>
            {
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
            },
        }, new CustomResourceOptions { Parent = this });

        // Build API keys JSON from config
        // Note: We use Output.All to handle the potentially null optional keys
        var apiKeysJson = Output.All(
            config.LlmApiKey ?? Output.Create(""),
            config.DeepgramApiKey ?? Output.Create(""),
            config.ElevenlabsApiKey ?? Output.Create(""),
            config.LangfuseSecretKey ?? Output.Create("")
        ).Apply(keys =>
        {
            var dict = new Dictionary<string, string>();

            // Only include if provided
            if (!string.IsNullOrEmpty(keys[0]))
                dict["LLM_API_KEY"] = keys[0];

            if (!string.IsNullOrEmpty(keys[1]))
                dict["DEEPGRAM_API_KEY"] = keys[1];

            if (!string.IsNullOrEmpty(keys[2]))
                dict["ELEVENLABS_API_KEY"] = keys[2];

            if (!string.IsNullOrEmpty(keys[3]))
                dict["LANGFUSE_SECRET_KEY"] = keys[3];

            return System.Text.Json.JsonSerializer.Serialize(dict);
        });

        new SecretVersion($"{name}-api-keys-version", new SecretVersionArgs
        {
            SecretId = apiKeysSecret.Id,
            SecretString = apiKeysJson,
        }, new CustomResourceOptions { Parent = this });

        ApiKeysSecretArn = apiKeysSecret.Arn;

        // ===================
        // Bootstrap Superuser Password
        // ===================

        var superuserSecret = new Secret($"{name}-superuser-password", new SecretArgs
        {
            Name = $"{config.ProjectName}/{config.Environment}/superuser-password",
            Description = "Bootstrap superuser password",
            Tags = new InputMap<string>
            {
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
            },
        }, new CustomResourceOptions { Parent = this });

        new SecretVersion($"{name}-superuser-password-version", new SecretVersionArgs
        {
            SecretId = superuserSecret.Id,
            SecretString = config.SuperuserPassword,
        }, new CustomResourceOptions { Parent = this });

        SuperuserPasswordSecretArn = superuserSecret.Arn;

        RegisterOutputs();
    }
}
