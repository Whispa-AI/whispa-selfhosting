using System.Text.Json;
using Pulumi;
using Pulumi.Aws.CloudWatch;
using Pulumi.Aws.CloudWatch.Inputs;
using Whispa.Aws.Pulumi.Configuration;

namespace Whispa.Aws.Pulumi.Components;

/// <summary>
/// Notifies the alert topic when an ECS deployment of the backend or frontend
/// fails. The deployment circuit breaker rolls a failed deployment back on its
/// own, so without this nobody hears about it: the previous version keeps
/// serving, possibly against a newer frontend or an already-migrated database.
/// Covers every deployment, not just the ones `pulumi up` waits for.
/// </summary>
public class DeploymentAlertsStack : ComponentResource
{
    public DeploymentAlertsStack(
        string name,
        WhispaConfig config,
        Output<string> alertTopicArn,
        Output<string> backendServiceArn,
        Output<string> frontendServiceArn,
        ComponentResourceOptions? options = null)
        : base("whispa:monitoring:DeploymentAlertsStack", name, options)
    {
        var ruleName = config.ResourceName("deployment-failed");

        var rule = new EventRule($"{name}-rule", new EventRuleArgs
        {
            Name = ruleName,
            Description = "Alerts when an ECS deployment of the Whispa backend or frontend fails",
            EventPattern = Output.Tuple(backendServiceArn, frontendServiceArn)
                .Apply(arns => EventPattern(arns.Item1, arns.Item2)),
            Tags = new InputMap<string>
            {
                ["Name"] = ruleName,
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
            },
        }, new CustomResourceOptions { Parent = this });

        new EventTarget($"{name}-target", new EventTargetArgs
        {
            Rule = rule.Name,
            Arn = alertTopicArn,
            InputTransformer = new EventTargetInputTransformerArgs
            {
                InputPaths = new InputMap<string>
                {
                    ["service"] = "$.resources[0]",
                    ["reason"] = "$.detail.reason",
                    ["deployment"] = "$.detail.deploymentId",
                    ["time"] = "$.time",
                },
                InputTemplate = MessageTemplate(config.EffectivePrefix),
            },
        }, new CustomResourceOptions { Parent = this });

        RegisterOutputs();
    }

    /// <summary>
    /// EventBridge pattern matching failed deployments of the given services.
    /// ECS emits SERVICE_DEPLOYMENT_FAILED when the circuit breaker trips.
    /// </summary>
    public static string EventPattern(params string[] serviceArns) =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["source"] = new[] { "aws.ecs" },
            ["detail-type"] = new[] { "ECS Deployment State Change" },
            ["resources"] = serviceArns,
            ["detail"] = new Dictionary<string, object>
            {
                ["eventName"] = new[] { "SERVICE_DEPLOYMENT_FAILED" },
            },
        });

    /// <summary>
    /// A plain-text notification (a quoted JSON string, so email subscribers
    /// get prose rather than the raw event).
    /// </summary>
    public static string MessageTemplate(string stack) =>
        JsonSerializer.Serialize(
            $"Whispa deployment FAILED ({stack}): <service>, deployment <deployment> at <time>. " +
            "Reason: <reason>. " +
            "ECS rolled the service back to its previous version, which is still serving; " +
            "the backend and frontend may now run different releases, and the database may already be migrated. " +
            "Check the stopped tasks' logs, fix the cause and redeploy with `pulumi up --refresh` " +
            "(not \"Force new deployment\", which redeploys the old version).",
            new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
}
