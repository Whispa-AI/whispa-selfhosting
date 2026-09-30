using Whispa.Aws.Pulumi.Configuration;
using Xunit;

namespace Whispa.Aws.Pulumi.Tests;

/// <summary>
/// Monitoring ports and the voice agent's SIP/RTP ports share one budget: every
/// forwarded port is a target group on the backend service, and ECS permits five
/// per service with the ALB already using one. A configuration that exceeds it
/// fails mid-deploy, so it is rejected at preview time instead.
/// </summary>
public class MediaPortValidationTests
{
    [Fact]
    public void MonitoringPortsAloneAreAccepted()
    {
        WhispaConfig.ValidateMediaPorts([42010, 42011], []);
    }

    [Fact]
    public void OneVoiceAgentFitsBesideTheMonitoringPorts()
    {
        WhispaConfig.ValidateMediaPorts([42010, 42011], [5062, 16000]);
    }

    [Fact]
    public void MonitoringAndVoiceAgentPortsMustFitTheTargetGroupBudget()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            WhispaConfig.ValidateMediaPorts([42010, 42011, 42012], [5062, 16000]));
        Assert.Contains("including the voice agent's 2", error.Message);
    }

    [Theory]
    [InlineData(42010, 16000)]
    [InlineData(5062, 5062)]
    public void VoiceAgentPortsMustNotReuseAnotherForwardedPort(int sip, int rtp)
    {
        Assert.Throws<InvalidOperationException>(() =>
            WhispaConfig.ValidateMediaPorts([42010, 42011], [sip, rtp]));
    }

    [Fact]
    public void VoiceAgentPortsMustBeValid()
    {
        Assert.Throws<InvalidOperationException>(() =>
            WhispaConfig.ValidateMediaPorts([42010, 42011], [0, 16000]));
    }

    [Fact]
    public void MonitoringPortsRemainRequired()
    {
        // The backend's TCN integration will not start without them, and with
        // it neither does the voice agent.
        Assert.Throws<InvalidOperationException>(() =>
            WhispaConfig.ValidateMediaPorts([], [5062, 16000]));
    }
}
