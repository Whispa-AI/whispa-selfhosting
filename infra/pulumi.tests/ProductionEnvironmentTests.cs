using Whispa.Aws.Pulumi.Configuration;
using Xunit;

namespace Whispa.Aws.Pulumi.Tests;

/// <summary>
/// Which environments count as production. A production environment that is
/// not recognised loses deletion protection on its database and load balancer
/// and is deleted without a final snapshot.
/// </summary>
public class ProductionEnvironmentTests
{
    [Theory]
    [InlineData("prod")]
    [InlineData("cc-prod")]
    [InlineData("acme-prod")]
    public void Names_Ending_In_Prod_Are_Production(string environment)
    {
        Assert.True(WhispaConfig.IsProductionEnvironment(environment));
    }

    [Theory]
    [InlineData("dev")]
    [InlineData("test")]
    [InlineData("cc-test")]
    [InlineData("prod-copy")]
    public void Other_Names_Are_Not_Production(string environment)
    {
        Assert.False(WhispaConfig.IsProductionEnvironment(environment));
    }
}
