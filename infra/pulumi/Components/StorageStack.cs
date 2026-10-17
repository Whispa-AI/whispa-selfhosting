using System.Text.Json;
using Pulumi;
using Pulumi.Aws.Iam;
using Pulumi.Aws.S3;
using Pulumi.Aws.S3.Inputs;
using Whispa.Aws.Pulumi.Configuration;

namespace Whispa.Aws.Pulumi.Components;

/// <summary>
/// Creates S3 storage infrastructure:
/// - Audio storage bucket (for call recordings and batch transcription)
/// - Bucket policies and CORS configuration
/// - Lifecycle rules for cost management
/// - Optional cross-region replica of the audio bucket
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
        // Create audio storage bucket
        var audioBucket = new Bucket($"{name}-audio", new BucketArgs
        {
            // Bucket names must be globally unique; Pulumi appends a random suffix
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

        var audioVersioning = SecureBucket($"{name}-audio", audioBucket, new CustomResourceOptions { Parent = this });

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

        if (!string.IsNullOrWhiteSpace(config.AudioReplicaRegion))
        {
            ConfigureReplica(name, config, config.AudioReplicaRegion, audioBucket, audioVersioning);
        }

        RegisterOutputs();
    }

    /// <summary>
    /// Applies the settings every audio bucket gets: public access blocked,
    /// versioning, encryption at rest, HTTPS-only access and lifecycle rules.
    /// Returns the versioning resource, which replication must wait for.
    /// </summary>
    private static BucketVersioningV2 SecureBucket(string prefix, Bucket bucket, CustomResourceOptions options)
    {
        // Block all public access (security best practice)
        var publicAccessBlock = new BucketPublicAccessBlock($"{prefix}-public-access", new BucketPublicAccessBlockArgs
        {
            Bucket = bucket.Id,
            BlockPublicAcls = true,
            BlockPublicPolicy = true,
            IgnorePublicAcls = true,
            RestrictPublicBuckets = true,
        }, options);

        // Enable versioning (for data protection)
        var versioning = new BucketVersioningV2($"{prefix}-versioning", new BucketVersioningV2Args
        {
            Bucket = bucket.Id,
            VersioningConfiguration = new BucketVersioningV2VersioningConfigurationArgs
            {
                Status = "Enabled",
            },
        }, options);

        // Enable server-side encryption
        new BucketServerSideEncryptionConfigurationV2($"{prefix}-encryption", new BucketServerSideEncryptionConfigurationV2Args
        {
            Bucket = bucket.Id,
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
        }, options);

        // Refuse plain-HTTP requests. S3 rejects a policy written while the
        // public access block is still being applied, so wait for it.
        new BucketPolicy($"{prefix}-https-only", new BucketPolicyArgs
        {
            Bucket = bucket.Id,
            Policy = bucket.Arn.Apply(HttpsOnlyPolicy),
        }, CustomResourceOptions.Merge(options, new CustomResourceOptions { DependsOn = { publicAccessBlock } }));

        // Lifecycle rules for cost management
        new BucketLifecycleConfigurationV2($"{prefix}-lifecycle", new BucketLifecycleConfigurationV2Args
        {
            Bucket = bucket.Id,
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
        }, options);

        return versioning;
    }

    /// <summary>
    /// Replicates the audio bucket to a bucket in another region, so recordings
    /// survive the loss of the deployment region. Deletes replicate as delete
    /// markers, so the replica keeps the source's retention: a deleted
    /// recording becomes a noncurrent version there and expires on the same
    /// lifecycle rule. Only objects written after replication is turned on are
    /// copied.
    /// </summary>
    private void ConfigureReplica(
        string name, WhispaConfig config, string replicaRegion, Bucket sourceBucket, BucketVersioningV2 sourceVersioning)
    {
        if (replicaRegion == config.AwsRegion)
        {
            throw new InvalidOperationException(
                $"audioReplicaRegion must differ from aws:region ({config.AwsRegion}); a replica in the same region does not survive a regional outage.");
        }

        // Explicit providers don't read the aws: stack config, so pass on the
        // profile the default provider deploys with.
        var replicaProvider = new global::Pulumi.Aws.Provider($"{name}-audio-replica-provider", new global::Pulumi.Aws.ProviderArgs
        {
            Region = replicaRegion,
            Profile = config.AwsProfile,
        }, new CustomResourceOptions { Parent = this });

        var replicaOptions = new CustomResourceOptions { Parent = this, Provider = replicaProvider };

        var replicaBucket = new Bucket($"{name}-audio-replica", new BucketArgs
        {
            BucketPrefix = $"{config.ProjectName}-{config.Environment}-audio-replica-",
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("audio-replica"),
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
            },
        }, replicaOptions);

        var replicaVersioning = SecureBucket($"{name}-audio-replica", replicaBucket, replicaOptions);

        var replicationRole = new Role($"{name}-audio-replication-role", new RoleArgs
        {
            Name = config.ResourceName("audio-replication"),
            AssumeRolePolicy = JsonSerializer.Serialize(new
            {
                Version = "2012-10-17",
                Statement = new[]
                {
                    new
                    {
                        Effect = "Allow",
                        Principal = new { Service = "s3.amazonaws.com" },
                        Action = "sts:AssumeRole",
                    },
                },
            }),
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("audio-replication"),
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
            },
        }, new CustomResourceOptions { Parent = this });

        new RolePolicy($"{name}-audio-replication-policy", new RolePolicyArgs
        {
            Role = replicationRole.Name,
            Policy = Output.Tuple(sourceBucket.Arn, replicaBucket.Arn)
                .Apply(arns => ReplicationPolicy(arns.Item1, arns.Item2)),
        }, new CustomResourceOptions { Parent = this });

        new BucketReplicationConfig($"{name}-audio-replication", new BucketReplicationConfigArgs
        {
            Bucket = sourceBucket.Id,
            Role = replicationRole.Arn,
            Rules = new[]
            {
                new BucketReplicationConfigRuleArgs
                {
                    Id = "replicate-all",
                    Status = "Enabled",
                    // An empty filter selects every object; it is also what
                    // allows delete markers to replicate.
                    Filter = new BucketReplicationConfigRuleFilterArgs(),
                    DeleteMarkerReplication = new BucketReplicationConfigRuleDeleteMarkerReplicationArgs
                    {
                        Status = "Enabled",
                    },
                    Destination = new BucketReplicationConfigRuleDestinationArgs
                    {
                        Bucket = replicaBucket.Arn,
                        StorageClass = "STANDARD_IA",
                    },
                },
            },
        }, new CustomResourceOptions
        {
            Parent = this,
            // S3 refuses a replication configuration until both buckets are versioned.
            DependsOn = { sourceVersioning, replicaVersioning },
        });
    }

    /// <summary>
    /// Bucket policy that denies any request not made over HTTPS. "Bool", not
    /// "BoolIfExists": every S3 request carries aws:SecureTransport, and the
    /// stricter form is what compliance scanners look for.
    /// </summary>
    public static string HttpsOnlyPolicy(string bucketArn) =>
        JsonSerializer.Serialize(new
        {
            Version = "2012-10-17",
            Statement = new[]
            {
                new
                {
                    Sid = "DenyInsecureTransport",
                    Effect = "Deny",
                    Principal = "*",
                    Action = "s3:*",
                    Resource = new[] { bucketArn, $"{bucketArn}/*" },
                    Condition = new
                    {
                        Bool = new Dictionary<string, string> { ["aws:SecureTransport"] = "false" },
                    },
                },
            },
        });

    /// <summary>
    /// Permissions S3 needs to replicate from the source bucket to the replica.
    /// </summary>
    public static string ReplicationPolicy(string sourceBucketArn, string replicaBucketArn) =>
        JsonSerializer.Serialize(new
        {
            Version = "2012-10-17",
            Statement = new object[]
            {
                new
                {
                    Sid = "ReadSourceConfiguration",
                    Effect = "Allow",
                    Action = new[] { "s3:GetReplicationConfiguration", "s3:ListBucket" },
                    Resource = sourceBucketArn,
                },
                new
                {
                    Sid = "ReadSourceObjects",
                    Effect = "Allow",
                    Action = new[]
                    {
                        "s3:GetObjectVersionForReplication", "s3:GetObjectVersionAcl", "s3:GetObjectVersionTagging",
                    },
                    Resource = $"{sourceBucketArn}/*",
                },
                new
                {
                    Sid = "WriteReplica",
                    Effect = "Allow",
                    Action = new[] { "s3:ReplicateObject", "s3:ReplicateDelete", "s3:ReplicateTags" },
                    Resource = $"{replicaBucketArn}/*",
                },
            },
        });
}
