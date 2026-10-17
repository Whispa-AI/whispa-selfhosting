using Pulumi;
using Whispa.Aws.Pulumi.Configuration;

namespace Whispa.Aws.Pulumi.Components;

/// <summary>
/// Notifies the alert topic when the application load balancer sees slow
/// responses or server errors, or a target group has an unhealthy target. ECS
/// replaces failing tasks on its own, so without these a degraded or flapping
/// service goes unnoticed until users report it.
/// </summary>
public class LoadBalancerAlarmsStack : ComponentResource
{
    public const string Namespace = "AWS/ApplicationELB";

    /// <summary>
    /// One alarm. Threshold is in the metric's unit (seconds for response
    /// time, a count otherwise); every period is 5 minutes.
    /// </summary>
    public sealed record Alarm(
        string Name, string MetricName, string Statistic, double Threshold, int EvaluationPeriods, string Description);

    /// <summary>Alarms on the load balancer as a whole</summary>
    public static readonly Alarm[] LoadBalancerAlarms =
    [
        new("target-response-time-high", "TargetResponseTime", "p90", 2, 3,
            "90th percentile response time above 2 s for 15 minutes"),
        new("target-5xx-high", "HTTPCode_Target_5XX_Count", "Sum", 10, 1,
            "More than 10 5XX responses from the backend or frontend in 5 minutes"),
        new("elb-5xx-high", "HTTPCode_ELB_5XX_Count", "Sum", 10, 1,
            "More than 10 5XX responses from the load balancer itself in 5 minutes (no healthy target, or a target timed out)"),
    ];

    /// <summary>Alarm on each target group</summary>
    public static readonly Alarm UnhealthyTargets = new(
        "unhealthy-targets", "UnHealthyHostCount", "Maximum", 0, 1,
        "A target failed its health checks in the last 5 minutes");

    public LoadBalancerAlarmsStack(
        string name,
        WhispaConfig config,
        Output<string> alertTopicArn,
        Output<string> loadBalancerArnSuffix,
        IReadOnlyDictionary<string, Output<string>> targetGroupArnSuffixes,
        ComponentResourceOptions? options = null)
        : base("whispa:monitoring:LoadBalancerAlarmsStack", name, options)
    {
        foreach (var alarm in LoadBalancerAlarms)
        {
            Create($"{name}-{alarm.Name}", config.ResourceName($"alb-{alarm.Name}"), alarm, config, alertTopicArn,
                new InputMap<string> { ["LoadBalancer"] = loadBalancerArnSuffix });
        }

        foreach (var (service, targetGroupArnSuffix) in targetGroupArnSuffixes)
        {
            // Target group metrics are published per load balancer, so the
            // alarm needs both dimensions to match any data.
            Create($"{name}-{service}-{UnhealthyTargets.Name}", config.ResourceName($"{service}-{UnhealthyTargets.Name}"),
                UnhealthyTargets, config, alertTopicArn,
                new InputMap<string>
                {
                    ["LoadBalancer"] = loadBalancerArnSuffix,
                    ["TargetGroup"] = targetGroupArnSuffix,
                });
        }

        RegisterOutputs();
    }

    private void Create(
        string resourceName, string alarmName, Alarm alarm, WhispaConfig config,
        Output<string> alertTopicArn, InputMap<string> dimensions) =>
        MetricAlarms.Create(
            this,
            resourceName,
            alarmName,
            Namespace,
            alarm.MetricName,
            dimensions,
            alarm.Threshold,
            alarm.Description,
            new InputMap<string>
            {
                ["Name"] = alarmName,
                ["Metric"] = alarm.MetricName,
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
            },
            alertTopicArn,
            statistic: alarm.Statistic,
            evaluationPeriods: alarm.EvaluationPeriods,
            // No requests means no data, not a problem.
            treatMissingData: "notBreaching");
}
