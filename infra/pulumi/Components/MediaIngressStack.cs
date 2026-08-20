using Pulumi;
using Pulumi.Aws.Ec2;
using Pulumi.Aws.LB;
using Pulumi.Aws.LB.Inputs;
using System.Collections.Immutable;
using Whispa.Aws.Pulumi.Configuration;

namespace Whispa.Aws.Pulumi.Components;

/// <summary>
/// Optional inbound UDP media path for real-time call audio.
///
/// Some telephony providers stream live call audio to the backend as RTP rather
/// than over HTTP. That cannot arrive through the ALB — an Application Load
/// Balancer is HTTP-only and cannot forward UDP at all — so this component adds
/// a Network Load Balancer with UDP listeners in front of the same ECS service.
///
/// Disabled by default; enable per deployment with <c>mediaIngressEnabled</c>.
///
/// Three design points worth knowing before changing this:
///
/// 1. <b>A static Elastic IP, not just the load balancer's DNS name.</b> The
///    address is published to the provider inside an SDP body, which carries an
///    IP literal and not a hostname, and providers commonly allowlist it. An
///    auto-assigned address would also change and silently break media.
///
/// 2. <b>One node plus cross-zone routing.</b> Only one address can be
///    advertised, so the load balancer is placed in a single subnet and
///    cross-zone load balancing is enabled, letting that node reach the task in
///    whichever availability zone it happens to be running.
///
/// 3. <b>A fixed, small set of ports.</b> Only pre-registered ports are
///    forwarded, so the application shares one port pair across all concurrent
///    calls and distinguishes them by source address (the load balancer
///    preserves it). Two listeners therefore serve any number of calls, instead
///    of needing a pair per concurrent call.
///
/// The health check is HTTP against the application's existing health endpoint:
/// a UDP target group cannot health-check over UDP, and an unhealthy target
/// silently receives nothing at all.
/// </summary>
public class MediaIngressStack : ComponentResource
{
    /// <summary>Static address to advertise to the media provider.</summary>
    public Output<string> AdvertiseAddress { get; }

    /// <summary>Load balancer DNS name (diagnostics; SDP needs the address).</summary>
    public Output<string> DnsName { get; }

    /// <summary>Target group per media port, in the configured port order.</summary>
    public Output<ImmutableArray<string>> TargetGroupArns { get; }

    /// <summary>The UDP ports forwarded to the task, in order.</summary>
    public int[] Ports { get; }

    public MediaIngressStack(
        string name,
        WhispaConfig config,
        Output<string> vpcId,
        Output<ImmutableArray<string>> publicSubnetIds,
        int healthCheckPort,
        ComponentResourceOptions? options = null)
        : base("whispa:index:MediaIngressStack", name, options)
    {
        Ports = config.MediaIngressPorts;

        var eip = new Eip($"{name}-eip", new EipArgs
        {
            Domain = "vpc",
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("media-eip"),
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
            },
        }, new CustomResourceOptions { Parent = this });

        AdvertiseAddress = eip.PublicIp;

        var loadBalancer = new LoadBalancer($"{name}-nlb", new LoadBalancerArgs
        {
            Name = config.ResourceName("media"),
            LoadBalancerType = "network",
            Internal = false,
            // A single node keeps a single advertisable address; cross-zone
            // routing is what lets it still reach a task in another zone.
            SubnetMappings = publicSubnetIds.Apply(ids => new[]
            {
                new LoadBalancerSubnetMappingArgs
                {
                    SubnetId = ids[0],
                    AllocationId = eip.Id,
                },
            }.ToList()),
            EnableCrossZoneLoadBalancing = true,
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("media-nlb"),
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
            },
        }, new CustomResourceOptions { Parent = this });

        DnsName = loadBalancer.DnsName;

        var targetGroupArns = new List<Output<string>>();
        for (var index = 0; index < Ports.Length; index++)
        {
            var port = Ports[index];
            var targetGroup = new TargetGroup($"{name}-tg-{port}", new TargetGroupArgs
            {
                Name = TruncateName(config.ProjectName, config.Environment, $"m{index}"),
                Port = port,
                Protocol = "UDP",
                VpcId = vpcId,
                TargetType = "ip",
                // UDP target groups cannot health-check over UDP, and an
                // unhealthy target receives no traffic whatsoever — so this
                // borrows the application's HTTP health endpoint.
                HealthCheck = new TargetGroupHealthCheckArgs
                {
                    Protocol = "HTTP",
                    Port = healthCheckPort.ToString(),
                    Path = "/health",
                    Matcher = "200",
                    Interval = 30,
                    Timeout = 10,
                    HealthyThreshold = 2,
                    UnhealthyThreshold = 3,
                },
                Tags = new InputMap<string>
                {
                    ["Name"] = config.ResourceName($"media-tg-{port}"),
                    ["Project"] = config.ProjectName,
                    ["Environment"] = config.Environment,
                },
            }, new CustomResourceOptions { Parent = this });

            _ = new Listener($"{name}-listener-{port}", new ListenerArgs
            {
                LoadBalancerArn = loadBalancer.Arn,
                Port = port,
                Protocol = "UDP",
                DefaultActions = new[]
                {
                    new ListenerDefaultActionArgs
                    {
                        Type = "forward",
                        TargetGroupArn = targetGroup.Arn,
                    },
                },
            }, new CustomResourceOptions { Parent = this });

            targetGroupArns.Add(targetGroup.Arn);
        }

        TargetGroupArns = Output.All(targetGroupArns.ToArray())
            .Apply(arns => arns.ToImmutableArray());

        RegisterOutputs(new Dictionary<string, object?>
        {
            ["advertiseAddress"] = AdvertiseAddress,
            ["dnsName"] = DnsName,
            ["targetGroupArns"] = TargetGroupArns,
        });
    }

    /// <summary>Target group names are capped at 32 characters and must be unique.</summary>
    private static string TruncateName(string project, string environment, string suffix)
    {
        var candidate = $"{project}-{environment}-{suffix}";
        return candidate.Length <= 32 ? candidate : candidate[^32..].TrimStart('-');
    }
}
