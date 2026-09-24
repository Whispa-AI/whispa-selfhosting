namespace Whispa.RolloutGate;

/// <summary>What one poll of the ECS service says about the rollout.</summary>
public abstract record RolloutVerdict
{
    public sealed record Completed : RolloutVerdict;
    public sealed record Failed(string Reason) : RolloutVerdict;
    public sealed record InProgress(string State) : RolloutVerdict;
}

/// <summary>One ECS deployment as reported by DescribeServices.</summary>
public sealed record DeploymentSnapshot(
    string Status, string TaskDefinition, string? RolloutState, string? RolloutStateReason);

/// <summary>
/// Pure decision logic, kept apart from the AWS calls so it is unit-testable.
/// </summary>
public static class RolloutDecision
{
    public static RolloutVerdict Evaluate(
        IReadOnlyList<DeploymentSnapshot> deployments, string expectedTaskDefinition)
    {
        var primary = deployments.FirstOrDefault(d => d.Status == "PRIMARY");
        var ours = deployments.FirstOrDefault(d => d.TaskDefinition == expectedTaskDefinition);

        if (primary is null || primary.TaskDefinition != expectedTaskDefinition)
        {
            // Rolled back (the circuit breaker makes the previous revision
            // primary again) or superseded by a concurrent deploy. Either way,
            // not ours.
            var detail = ours is null
                ? "the deployment is no longer present"
                : $"rollout: {ours.RolloutState ?? "unknown"}, {ours.RolloutStateReason ?? "no reason given"}";
            return new RolloutVerdict.Failed(
                $"rollout of {ShortName(expectedTaskDefinition)} did not complete; the service is running " +
                $"{ShortName(primary?.TaskDefinition ?? "no primary deployment")} ({detail})");
        }

        return primary.RolloutState switch
        {
            "COMPLETED" => new RolloutVerdict.Completed(),
            "FAILED" => new RolloutVerdict.Failed(
                $"rollout of {ShortName(expectedTaskDefinition)} failed: {primary.RolloutStateReason ?? "no reason given"}"),
            var state => new RolloutVerdict.InProgress(state ?? "UNKNOWN"),
        };
    }

    /// <summary>"arn:...:task-definition/family:7" → "family:7".</summary>
    public static string ShortName(string taskDefinition) =>
        taskDefinition[(taskDefinition.LastIndexOf('/') + 1)..];
}
