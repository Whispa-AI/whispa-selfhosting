using System;
using System.Linq;
using Pulumi;
using Pulumi.Aws;
using Pulumi.Aws.Iam;
using System.Text.Json;
using Whispa.Aws.Pulumi.Configuration;

namespace Whispa.Aws.Pulumi.Components;

/// <summary>
/// Creates IAM roles and policies for ECS tasks:
/// - Task Execution Role (used by ECS agent to pull images, read secrets)
/// - Task Role (used by application to access AWS services like S3, SES)
/// </summary>
public class IamStack : ComponentResource
{
    /// <summary>ECS Task Execution Role ARN</summary>
    public Output<string> TaskExecutionRoleArn { get; }

    /// <summary>ECS Task Role ARN</summary>
    public Output<string> TaskRoleArn { get; }

    public IamStack(
        string name,
        WhispaConfig config,
        Output<string> audioBucketArn,
        Output<string> dbPasswordSecretArn,
        Output<string> appSecretsArn,
        Output<string> apiKeysSecretArn,
        Output<string> backendLogGroupArn,
        Output<string> frontendLogGroupArn,
        Output<string> superuserPasswordSecretArn,
        ComponentResourceOptions? options = null)
        : base("whispa:iam:IamStack", name, options)
    {
        // ECS tasks assume role trust policy
        var ecsTasksAssumeRolePolicy = JsonSerializer.Serialize(new
        {
            Version = "2012-10-17",
            Statement = new[]
            {
                new
                {
                    Effect = "Allow",
                    Principal = new { Service = "ecs-tasks.amazonaws.com" },
                    Action = "sts:AssumeRole",
                },
            },
        });

        // ===================
        // Task Execution Role
        // ===================
        // Used by ECS agent to:
        // - Pull container images
        // - Write logs to CloudWatch
        // - Read secrets from Secrets Manager

        var taskExecutionRole = new Role($"{name}-task-execution-role", new RoleArgs
        {
            Name = config.ResourceName("ecs-task-execution"),
            AssumeRolePolicy = ecsTasksAssumeRolePolicy,
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("ecs-task-execution"),
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
            },
        }, new CustomResourceOptions { Parent = this });

        // Attach AWS managed policy for basic ECS task execution
        new RolePolicyAttachment($"{name}-task-execution-managed", new RolePolicyAttachmentArgs
        {
            Role = taskExecutionRole.Name,
            PolicyArn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy",
        }, new CustomResourceOptions { Parent = this });

        // Custom policy for reading secrets
        var secretsReadPolicy = Output.All(dbPasswordSecretArn, appSecretsArn, apiKeysSecretArn, superuserPasswordSecretArn)
            .Apply(arns =>
            {
                // Filter out empty ARNs (superuser secret is optional)
                var resources = arns.Where(arn => !string.IsNullOrEmpty(arn)).ToArray();
                return JsonSerializer.Serialize(new
                {
                    Version = "2012-10-17",
                    Statement = new[]
                    {
                        new
                        {
                            Sid = "ReadSecrets",
                            Effect = "Allow",
                            Action = new[] { "secretsmanager:GetSecretValue" },
                            Resource = resources,
                        },
                    },
                });
            });

        new RolePolicy($"{name}-task-execution-secrets", new RolePolicyArgs
        {
            Role = taskExecutionRole.Name,
            Policy = secretsReadPolicy,
        }, new CustomResourceOptions { Parent = this });

        TaskExecutionRoleArn = taskExecutionRole.Arn;

        // ===================
        // Task Role
        // ===================
        // Used by the application to:
        // - Send emails via SES
        // - Read/write to S3 audio bucket
        // - (Optional) Access Kinesis Video Streams for AWS Connect

        var taskRole = new Role($"{name}-task-role", new RoleArgs
        {
            Name = config.ResourceName("ecs-task"),
            AssumeRolePolicy = ecsTasksAssumeRolePolicy,
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("ecs-task"),
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
            },
        }, new CustomResourceOptions { Parent = this });

        // SES email sending policy
        var sesPolicy = JsonSerializer.Serialize(new
        {
            Version = "2012-10-17",
            Statement = new[]
            {
                new
                {
                    Sid = "SendEmail",
                    Effect = "Allow",
                    Action = new[] { "ses:SendEmail", "ses:SendRawEmail" },
                    Resource = "*",
                    Condition = new
                    {
                        StringEquals = new Dictionary<string, string>
                        {
                            ["ses:FromAddress"] = config.MailFrom,
                        },
                    },
                },
            },
        });

        new RolePolicy($"{name}-task-ses", new RolePolicyArgs
        {
            Role = taskRole.Name,
            Policy = sesPolicy,
        }, new CustomResourceOptions { Parent = this });

        // S3 audio bucket policy
        var s3Policy = audioBucketArn.Apply(bucketArn => JsonSerializer.Serialize(new
        {
            Version = "2012-10-17",
            Statement = new[]
            {
                new
                {
                    Sid = "AudioBucketAccess",
                    Effect = "Allow",
                    Action = new[]
                    {
                        "s3:GetObject",
                        "s3:PutObject",
                        "s3:DeleteObject",
                        "s3:ListBucket",
                    },
                    Resource = new[]
                    {
                        bucketArn,
                        $"{bucketArn}/*",
                    },
                },
            },
        }));

        new RolePolicy($"{name}-task-s3", new RolePolicyArgs
        {
            Role = taskRole.Name,
            Policy = s3Policy,
        }, new CustomResourceOptions { Parent = this });

        // CloudWatch logs policy (for application logging)
        var logsPolicy = Output.All(backendLogGroupArn, frontendLogGroupArn)
            .Apply(arns => JsonSerializer.Serialize(new
            {
                Version = "2012-10-17",
                Statement = new[]
                {
                    new
                    {
                        Sid = "WriteLogs",
                        Effect = "Allow",
                        Action = new[]
                        {
                            "logs:CreateLogStream",
                            "logs:PutLogEvents",
                        },
                        Resource = arns.Select(arn => $"{arn}:*").ToArray(),
                    },
                },
            }));

        new RolePolicy($"{name}-task-logs", new RolePolicyArgs
        {
            Role = taskRole.Name,
            Policy = logsPolicy,
        }, new CustomResourceOptions { Parent = this });

        // ECS Exec policy (SSM messages)
        var ecsExecPolicy = JsonSerializer.Serialize(new
        {
            Version = "2012-10-17",
            Statement = new[]
            {
                new
                {
                    Sid = "EcsExec",
                    Effect = "Allow",
                    Action = new[]
                    {
                        "ssmmessages:CreateControlChannel",
                        "ssmmessages:CreateDataChannel",
                        "ssmmessages:OpenControlChannel",
                        "ssmmessages:OpenDataChannel",
                        "ssm:UpdateInstanceInformation",
                    },
                    Resource = "*",
                },
            },
        });

        new RolePolicy($"{name}-task-exec", new RolePolicyArgs
        {
            Role = taskRole.Name,
            Policy = ecsExecPolicy,
        }, new CustomResourceOptions { Parent = this });

        // AWS Bedrock policy (for bedrock/* LLM models)
        if (!string.IsNullOrWhiteSpace(config.BedrockRegion))
        {
            var bedrockPolicy = JsonSerializer.Serialize(new
            {
                Version = "2012-10-17",
                Statement = new[]
                {
                    new
                    {
                        Sid = "BedrockInvokeModel",
                        Effect = "Allow",
                        Action = new[]
                        {
                            "bedrock:InvokeModel",
                            "bedrock:InvokeModelWithResponseStream",
                        },
                        Resource = $"arn:aws:bedrock:{config.BedrockRegion}::foundation-model/*",
                    },
                },
            });

            new RolePolicy($"{name}-task-bedrock", new RolePolicyArgs
            {
                Role = taskRole.Name,
                Policy = bedrockPolicy,
            }, new CustomResourceOptions { Parent = this });
        }

        var provider = (config.TranscriptionProvider ?? string.Empty).Trim().ToLowerInvariant();
        var needsTranscribe = provider is "amazon" or "aws" or "transcribe" or "amazon-transcribe";

        if (config.EnableAwsConnect)
        {
            if (string.IsNullOrWhiteSpace(config.ConnectInstanceId))
            {
                throw new InvalidOperationException(
                    "enableAwsConnect is true but connectInstanceId is not set.");
            }

            var kvsPolicy = JsonSerializer.Serialize(new
            {
                Version = "2012-10-17",
                Statement = new[]
                {
                    new
                    {
                        Sid = "KinesisVideoListStreams",
                        Effect = "Allow",
                        Action = new[]
                        {
                            "kinesisvideo:ListStreams",
                        },
                        Resource = "*",
                    },
                    new
                    {
                        Sid = "KinesisVideoAccess",
                        Effect = "Allow",
                        Action = new[]
                        {
                            "kinesisvideo:GetDataEndpoint",
                            "kinesisvideo:GetMedia",
                            "kinesisvideo:DescribeStream",
                        },
                        Resource = $"arn:aws:kinesisvideo:*:*:stream/{config.KvsStreamPrefix}-*",
                    },
                },
            });

            new RolePolicy($"{name}-task-kvs", new RolePolicyArgs
            {
                Role = taskRole.Name,
                Policy = kvsPolicy,
            }, new CustomResourceOptions { Parent = this });

            var connectPolicy = Output.Create(GetCallerIdentity.InvokeAsync())
                .Apply(identity => JsonSerializer.Serialize(new
                {
                    Version = "2012-10-17",
                    Statement = new[]
                    {
                        new
                        {
                            Sid = "AWSConnectAccess",
                            Effect = "Allow",
                            Action = new[]
                            {
                                "connect:ListInstances",
                                "connect:DescribeInstance",
                                "connect:ListUsers",
                                "connect:DescribeUser",
                                "connect:GetContactAttributes",
                                "connect:StartContactStreaming",
                                "connect:StopContactStreaming",
                            },
                            Resource = new[]
                            {
                                $"arn:aws:connect:{config.AwsRegion}:{identity.AccountId}:instance/{config.ConnectInstanceId}",
                                $"arn:aws:connect:{config.AwsRegion}:{identity.AccountId}:instance/{config.ConnectInstanceId}/*",
                            },
                        },
                    },
                }));

            new RolePolicy($"{name}-task-connect", new RolePolicyArgs
            {
                Role = taskRole.Name,
                Policy = connectPolicy,
            }, new CustomResourceOptions { Parent = this });
        }

        if (needsTranscribe || config.EnableAwsConnect)
        {
            var transcribePolicy = JsonSerializer.Serialize(new
            {
                Version = "2012-10-17",
                Statement = new[]
                {
                    new
                    {
                        Sid = "TranscribeAccess",
                        Effect = "Allow",
                        Action = new[]
                        {
                            "transcribe:StartStreamTranscription",
                            "transcribe:StartStreamTranscriptionWebSocket",
                        },
                        Resource = "*",
                    },
                },
            });

            new RolePolicy($"{name}-task-transcribe", new RolePolicyArgs
            {
                Role = taskRole.Name,
                Policy = transcribePolicy,
            }, new CustomResourceOptions { Parent = this });
        }

        TaskRoleArn = taskRole.Arn;

        RegisterOutputs();
    }
}
