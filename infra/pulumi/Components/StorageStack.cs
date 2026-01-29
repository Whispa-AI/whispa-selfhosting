using Pulumi;
using Pulumi.Aws.S3;
using Pulumi.Aws.S3.Inputs;
using Whispa.Aws.Pulumi.Configuration;

namespace Whispa.Aws.Pulumi.Components;

/// <summary>
/// Creates S3 storage infrastructure:
/// - Audio storage bucket (for call recordings and batch transcription)
/// - Bucket policies and CORS configuration
/// - Lifecycle rules for cost management
/// </summary>
public class StorageStack : ComponentResource
{
    /// <summary>Audio bucket name</summary>
    public Output<string> AudioBucketName { get; }

    /// <summary>Audio bucket ARN</summary>
    public Output<string> AudioBucketArn { get; }

    /// <summary>Audio bucket regional domain name (for presigned URLs)</summary>
    public Output<string> AudioBucketDomainName { get; }

    public StorageStack(string name, WhispaConfig config, ComponentResourceOptions? options = null)
        : base("whispa:storage:StorageStack", name, options)
    {
        // Get AWS account ID for unique bucket naming
        var callerIdentity = global::Pulumi.Aws.GetCallerIdentity.Invoke();
        var accountId = callerIdentity.Apply(c => c.AccountId);

        // Create audio storage bucket with account ID suffix for uniqueness
        var audioBucket = new Bucket($"{name}-audio", new BucketArgs
        {
            // Bucket names must be globally unique - use account ID
            BucketPrefix = $"{config.ProjectName}-{config.Environment}-audio-",

            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("audio"),
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
            },
        }, new CustomResourceOptions { Parent = this });

        AudioBucketName = audioBucket.Id;
        AudioBucketArn = audioBucket.Arn;
        AudioBucketDomainName = audioBucket.BucketRegionalDomainName;

        // Block all public access (security best practice)
        new BucketPublicAccessBlock($"{name}-audio-public-access", new BucketPublicAccessBlockArgs
        {
            Bucket = audioBucket.Id,
            BlockPublicAcls = true,
            BlockPublicPolicy = true,
            IgnorePublicAcls = true,
            RestrictPublicBuckets = true,
        }, new CustomResourceOptions { Parent = this });

        // Enable versioning (for data protection)
        new BucketVersioningV2($"{name}-audio-versioning", new BucketVersioningV2Args
        {
            Bucket = audioBucket.Id,
            VersioningConfiguration = new BucketVersioningV2VersioningConfigurationArgs
            {
                Status = "Enabled",
            },
        }, new CustomResourceOptions { Parent = this });

        // Enable server-side encryption
        new BucketServerSideEncryptionConfigurationV2($"{name}-audio-encryption", new BucketServerSideEncryptionConfigurationV2Args
        {
            Bucket = audioBucket.Id,
            Rules = new[]
            {
                new BucketServerSideEncryptionConfigurationV2RuleArgs
                {
                    ApplyServerSideEncryptionByDefault = new BucketServerSideEncryptionConfigurationV2RuleApplyServerSideEncryptionByDefaultArgs
                    {
                        SseAlgorithm = "AES256",
                    },
                },
            },
        }, new CustomResourceOptions { Parent = this });

        // CORS configuration for presigned URL uploads from browser
        new BucketCorsConfigurationV2($"{name}-audio-cors", new BucketCorsConfigurationV2Args
        {
            Bucket = audioBucket.Id,
            CorsRules = new[]
            {
                new BucketCorsConfigurationV2CorsRuleArgs
                {
                    AllowedHeaders = new[] { "*" },
                    AllowedMethods = new[] { "GET", "PUT", "POST" },
                    AllowedOrigins = new[] { config.FrontendUrl },
                    ExposeHeaders = new[] { "ETag" },
                    MaxAgeSeconds = 3600,
                },
            },
        }, new CustomResourceOptions { Parent = this });

        // Lifecycle rules for cost management
        new BucketLifecycleConfigurationV2($"{name}-audio-lifecycle", new BucketLifecycleConfigurationV2Args
        {
            Bucket = audioBucket.Id,
            Rules = new[]
            {
                // Move old audio files to Infrequent Access after 30 days
                new BucketLifecycleConfigurationV2RuleArgs
                {
                    Id = "move-to-ia",
                    Status = "Enabled",
                    Filter = new BucketLifecycleConfigurationV2RuleFilterArgs
                    {
                        Prefix = "audio/",
                    },
                    Transitions = new[]
                    {
                        new BucketLifecycleConfigurationV2RuleTransitionArgs
                        {
                            Days = 30,
                            StorageClass = "STANDARD_IA",
                        },
                        new BucketLifecycleConfigurationV2RuleTransitionArgs
                        {
                            Days = 90,
                            StorageClass = "GLACIER",
                        },
                    },
                },
                // Delete non-current versions after 30 days
                new BucketLifecycleConfigurationV2RuleArgs
                {
                    Id = "cleanup-old-versions",
                    Status = "Enabled",
                    NoncurrentVersionExpiration = new BucketLifecycleConfigurationV2RuleNoncurrentVersionExpirationArgs
                    {
                        NoncurrentDays = 30,
                    },
                },
                // Delete incomplete multipart uploads after 7 days
                new BucketLifecycleConfigurationV2RuleArgs
                {
                    Id = "cleanup-incomplete-uploads",
                    Status = "Enabled",
                    AbortIncompleteMultipartUpload = new BucketLifecycleConfigurationV2RuleAbortIncompleteMultipartUploadArgs
                    {
                        DaysAfterInitiation = 7,
                    },
                },
            },
        }, new CustomResourceOptions { Parent = this });

        RegisterOutputs();
    }
}
