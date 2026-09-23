using System.Text.Json;
using Whispa.Aws.Pulumi.Components;
using Xunit;

namespace Whispa.Aws.Pulumi.Tests;

/// <summary>
/// Pins the failed-deployment alert: the EventBridge pattern must use ECS's exact
/// field names (a typo matches nothing, silently), and the message must stay a
/// plain JSON string so subscribers get prose, not the raw event.
/// </summary>
public class DeploymentAlertsTests
{
    private const string Backend = "arn:aws:ecs:ap-southeast-2:123456789012:service/whispa-prod-cluster/whispa-prod-backend";
    private const string Frontend = "arn:aws:ecs:ap-southeast-2:123456789012:service/whispa-prod-cluster/whispa-prod-frontend";

    [Fact]
    public void EventPattern_Matches_Failed_Deployments_Of_Both_Services()
    {
        var pattern = JsonDocument.Parse(DeploymentAlertsStack.EventPattern(Backend, Frontend)).RootElement;

        Assert.Equal("aws.ecs", pattern.GetProperty("source")[0].GetString());
        Assert.Equal("ECS Deployment State Change", pattern.GetProperty("detail-type")[0].GetString());
        Assert.Equal(
            new[] { Backend, Frontend },
            pattern.GetProperty("resources").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(
            "SERVICE_DEPLOYMENT_FAILED",
            pattern.GetProperty("detail").GetProperty("eventName")[0].GetString());
    }

    [Fact]
    public void MessageTemplate_Is_A_Json_String_With_Every_Placeholder()
    {
        var template = DeploymentAlertsStack.MessageTemplate("whispa-prod");

        var message = JsonSerializer.Deserialize<string>(template)!;
        Assert.StartsWith("Whispa deployment FAILED (whispa-prod): <service>", message);
        foreach (var placeholder in new[] { "<service>", "<deployment>", "<time>", "<reason>" })
        {
            // Unescaped in the raw template too, or EventBridge won't substitute it.
            Assert.Contains(placeholder, template);
        }
        Assert.Contains("pulumi up --refresh", message);
    }
}
