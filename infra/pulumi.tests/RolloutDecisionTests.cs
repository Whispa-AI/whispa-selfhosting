using Whispa.RolloutGate;
using Xunit;

namespace Whispa.Aws.Pulumi.Tests;

/// <summary>
/// The rollout gate's verdict per ECS DescribeServices poll. A wrong verdict
/// either blocks every deploy or lets a rolled-back release pass silently.
/// </summary>
public class RolloutDecisionTests
{
    private const string Family = "arn:aws:ecs:ap-southeast-2:123456789012:task-definition/whispa-prod-backend";
    private const string New = Family + ":8";
    private const string Previous = Family + ":7";

    private static DeploymentSnapshot Deployment(
        string status, string taskDefinition, string? state, string? reason = null) =>
        new(status, taskDefinition, state, reason);

    [Fact]
    public void Completed_Primary_For_The_New_Revision_Passes()
    {
        var verdict = RolloutDecision.Evaluate([Deployment("PRIMARY", New, "COMPLETED")], New);

        Assert.IsType<RolloutVerdict.Completed>(verdict);
    }

    [Fact]
    public void In_Progress_Primary_Keeps_Waiting()
    {
        var verdict = RolloutDecision.Evaluate(
            [Deployment("PRIMARY", New, "IN_PROGRESS"), Deployment("ACTIVE", Previous, "COMPLETED")], New);

        Assert.Equal("IN_PROGRESS", Assert.IsType<RolloutVerdict.InProgress>(verdict).State);
    }

    [Fact]
    public void Failed_Primary_Fails_With_Its_Reason()
    {
        var verdict = RolloutDecision.Evaluate(
            [Deployment("PRIMARY", New, "FAILED", "tasks failed to start")], New);

        Assert.Contains("tasks failed to start", Assert.IsType<RolloutVerdict.Failed>(verdict).Reason);
    }

    [Fact]
    public void Rollback_In_Progress_Fails_Naming_The_Circuit_Breaker_Reason()
    {
        // The circuit breaker made the previous revision primary again.
        var verdict = RolloutDecision.Evaluate(
        [
            Deployment("PRIMARY", Previous, "IN_PROGRESS"),
            Deployment("ACTIVE", New, "FAILED", "ECS deployment circuit breaker: tasks failed to start."),
        ], New);

        var reason = Assert.IsType<RolloutVerdict.Failed>(verdict).Reason;
        Assert.Contains("running whispa-prod-backend:7", reason);
        Assert.Contains("circuit breaker", reason);
    }

    [Fact]
    public void Finished_Rollback_Fails_Even_Though_The_Service_Is_Steady()
    {
        // The case the pinned provider's waitForSteadyState accepts as success.
        var verdict = RolloutDecision.Evaluate([Deployment("PRIMARY", Previous, "COMPLETED")], New);

        Assert.Contains("no longer present", Assert.IsType<RolloutVerdict.Failed>(verdict).Reason);
    }

    [Fact]
    public void No_Deployments_Fails()
    {
        Assert.IsType<RolloutVerdict.Failed>(RolloutDecision.Evaluate([], New));
    }

    [Fact]
    public void Short_Name_Keeps_Family_And_Revision()
    {
        Assert.Equal("whispa-prod-backend:8", RolloutDecision.ShortName(New));
    }
}
