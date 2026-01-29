using Pulumi;
using Pulumi.Aws.CloudWatch;
using Whispa.Aws.Pulumi.Configuration;

namespace Whispa.Aws.Pulumi.Components;

/// <summary>
/// Creates CloudWatch monitoring infrastructure:
/// - Log groups for ECS containers
/// - Log retention policies
/// </summary>
public class MonitoringStack : ComponentResource
{
    /// <summary>Backend log group name</summary>
    public Output<string> BackendLogGroupName { get; }

    /// <summary>Frontend log group name</summary>
    public Output<string> FrontendLogGroupName { get; }

    /// <summary>Backend log group ARN</summary>
    public Output<string> BackendLogGroupArn { get; }

    /// <summary>Frontend log group ARN</summary>
    public Output<string> FrontendLogGroupArn { get; }

    public MonitoringStack(string name, WhispaConfig config, ComponentResourceOptions? options = null)
        : base("whispa:monitoring:MonitoringStack", name, options)
    {
        // Retention period based on environment
        var retentionDays = config.Environment == "prod" ? 30 : 7;

        // Backend log group
        var backendLogGroup = new LogGroup($"{name}-backend-logs", new LogGroupArgs
        {
            Name = $"/ecs/{config.ProjectName}/{config.Environment}/backend",
            RetentionInDays = retentionDays,
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("backend-logs"),
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
                ["Service"] = "backend",
            },
        }, new CustomResourceOptions { Parent = this });

        BackendLogGroupName = backendLogGroup.Name;
        BackendLogGroupArn = backendLogGroup.Arn;

        // Frontend log group
        var frontendLogGroup = new LogGroup($"{name}-frontend-logs", new LogGroupArgs
        {
            Name = $"/ecs/{config.ProjectName}/{config.Environment}/frontend",
            RetentionInDays = retentionDays,
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("frontend-logs"),
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
                ["Service"] = "frontend",
            },
        }, new CustomResourceOptions { Parent = this });

        FrontendLogGroupName = frontendLogGroup.Name;
        FrontendLogGroupArn = frontendLogGroup.Arn;

        RegisterOutputs();
    }
}
