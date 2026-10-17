using System.Text.Json;
using Whispa.Aws.Pulumi.Components;
using Xunit;

namespace Whispa.Aws.Pulumi.Tests;

/// <summary>
/// Pins the VPC flow logs role's trust policy. The wrong service principal
/// leaves the flow log failing to deliver; a missing account condition lets
/// another account's flow logs use the role.
/// </summary>
public class FlowLogsTests
{
    [Fact]
    public void AssumeRolePolicy_Trusts_Flow_Logs_For_This_Account_Only()
    {
        var statement = JsonDocument.Parse(NetworkingStack.FlowLogsAssumeRolePolicy("123456789012"))
            .RootElement.GetProperty("Statement").EnumerateArray().Single();

        Assert.Equal("Allow", statement.GetProperty("Effect").GetString());
        Assert.Equal("sts:AssumeRole", statement.GetProperty("Action").GetString());
        Assert.Equal(
            "vpc-flow-logs.amazonaws.com",
            statement.GetProperty("Principal").GetProperty("Service").GetString());
        Assert.Equal(
            "123456789012",
            statement.GetProperty("Condition").GetProperty("StringEquals").GetProperty("aws:SourceAccount").GetString());
    }
}
