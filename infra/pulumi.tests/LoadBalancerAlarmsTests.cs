using Whispa.Aws.Pulumi.Components;
using Xunit;

namespace Whispa.Aws.Pulumi.Tests;

/// <summary>
/// Pins the load balancer alarms. CloudWatch accepts an alarm on a misspelt
/// metric or the wrong statistic and simply never fires, so a typo here
/// silently removes the alert.
/// </summary>
public class LoadBalancerAlarmsTests
{
    [Fact]
    public void Alarms_Use_The_Application_Load_Balancer_Namespace()
    {
        Assert.Equal("AWS/ApplicationELB", LoadBalancerAlarmsStack.Namespace);
    }

    [Theory]
    [InlineData("TargetResponseTime", "p90", 2, 3)]
    [InlineData("HTTPCode_Target_5XX_Count", "Sum", 10, 1)]
    [InlineData("HTTPCode_ELB_5XX_Count", "Sum", 10, 1)]
    public void Load_Balancer_Alarm_Matches_Its_Metric(
        string metricName, string statistic, double threshold, int evaluationPeriods)
    {
        var alarm = Assert.Single(LoadBalancerAlarmsStack.LoadBalancerAlarms, a => a.MetricName == metricName);

        Assert.Equal(statistic, alarm.Statistic);
        Assert.Equal(threshold, alarm.Threshold);
        Assert.Equal(evaluationPeriods, alarm.EvaluationPeriods);
    }

    [Fact]
    public void Unhealthy_Targets_Alarm_On_Any_Unhealthy_Host()
    {
        var alarm = LoadBalancerAlarmsStack.UnhealthyTargets;

        Assert.Equal("UnHealthyHostCount", alarm.MetricName);
        Assert.Equal("Maximum", alarm.Statistic);
        Assert.Equal(0, alarm.Threshold);
    }

    [Fact]
    public void Alarm_Names_Are_Unique()
    {
        var names = LoadBalancerAlarmsStack.LoadBalancerAlarms.Select(a => a.Name)
            .Append(LoadBalancerAlarmsStack.UnhealthyTargets.Name).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
    }
}
