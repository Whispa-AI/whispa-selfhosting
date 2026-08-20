using Pulumi;
using Pulumi.Aws.Ec2;
using Pulumi.Aws.Ec2.Inputs;
using Pulumi.Aws.LB;
using Pulumi.Aws.LB.Inputs;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
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
/// Design points worth knowing before changing this:
///
/// 1. <b>A static Elastic IP, not the load balancer's DNS name.</b> The address
///    is published to the provider inside an SDP body, which carries an IP
///    literal and not a hostname, and providers commonly allowlist it. An
///    auto-assigned address would also change and silently break media.
///
/// 2. <b>Every availability zone the service can run in must be enabled.</b>
///    Cross-zone load balancing only reaches targets in *enabled* zones — a
///    zone is enabled by having a subnet mapping — so a task placed in an
///    unmapped zone would be <c>Target.NotInUse</c> and receive nothing. With a
///    desired count of one that is a coin flip per deployment, presenting as
///    intermittent total silence. Every public subnet is therefore mapped, each
///    with its own Elastic IP, and only <see cref="AdvertiseAddress"/> (the
///    first) is ever published: the remaining nodes exist purely to enable
///    their zones, and cross-zone routing carries traffic from the advertised
///    node to wherever the task actually is.
///
/// 3. <b>A fixed, small set of ports.</b> Only pre-registered ports are
///    forwarded, so the application shares one port pair across all concurrent
///    calls and distinguishes them by source address (the load balancer
///    preserves it). Two listeners therefore serve any number of calls.
///
/// 4. <b>Health checks are HTTP against the application's own endpoint.</b> A
///    UDP target group cannot health-check over UDP. Note the health check
///    originates from the load balancer, not from a client, so the task's
///    security group must admit it — see <see cref="SecurityGroupId"/>. Without
///    that rule every target is unhealthy; because a Network Load Balancer
///    fails *open* when all targets are unhealthy, media may still flow, so the
///    symptom is silently broken health gating rather than an outage.
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

    /// <summary>
    /// The UDP listeners. The ECS service must depend on these: registering a
    /// target group that has no associated load balancer yet is rejected with
    /// "target group does not have an associated load balancer".
    /// </summary>
    public IReadOnlyList<Resource> Listeners { get; }

    public MediaIngressStack(
        string name,
        WhispaConfig config,
        Output<string> vpcId,
        Output<ImmutableArray<string>> publicSubnetIds,
        // Owned by NetworkingStack, which owns every security group.
        Input<string> securityGroupId,
        int healthCheckPort,
        ComponentResourceOptions? options = null)
        : base("whispa:index:MediaIngressStack", name, options)
    {
        Ports = config.MediaIngressPorts;
        var allowedCidrs = config.MediaIngressAllowedCidrs;


        // One Elastic IP per mapped zone. Only the first is advertised; the rest
        // exist so their zones are enabled (see note 2 above). Mixing allocated
        // and auto-assigned addresses across mappings is avoided deliberately.
        //
        // The addresses are created eagerly rather than inside an Apply over the
        // subnet ids: resources declared inside an Apply do not appear correctly
        // in `pulumi preview`. NetworkingStack always builds exactly
        // PublicSubnetCount subnets, so they can be indexed positionally.
        var eips = Enumerable.Range(0, NetworkingStack.PublicSubnetCount)
            .Select(index => new Eip($"{name}-eip-{index}", new EipArgs
            {
                Domain = "vpc",
                Tags = new InputMap<string>
                {
                    ["Name"] = config.ResourceName($"media-eip-{index}"),
                    ["Project"] = config.ProjectName,
                    ["Environment"] = config.Environment,
                },
            }, new CustomResourceOptions { Parent = this }))
            .ToList();

        AdvertiseAddress = eips[0].PublicIp;

        var subnetMappings = publicSubnetIds.Apply(ids =>
            eips.Select((eip, index) => new LoadBalancerSubnetMappingArgs
            {
                SubnetId = ids[index],
                AllocationId = eip.Id,
            }).ToList());

        var loadBalancer = new LoadBalancer($"{name}-nlb", new LoadBalancerArgs
        {
            Name = PhysicalName(config.EffectivePrefix, "media", 32),
            LoadBalancerType = "network",
            Internal = false,
            SubnetMappings = subnetMappings,
            SecurityGroups = new[] { securityGroupId },
            // Reaches targets in every *enabled* zone, which is why all public
            // subnets are mapped above.
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
        var listeners = new List<Resource>();
        foreach (var port in Ports)
        {
            var targetGroup = new TargetGroup($"{name}-tg-{port}", new TargetGroupArgs
            {
                // Keyed by port, so changing a port creates a distinctly named
                // group rather than colliding with the one being replaced.
                Name = PhysicalName(config.EffectivePrefix, $"m{port}", 32),
                Port = port,
                Protocol = "UDP",
                VpcId = vpcId,
                TargetType = "ip",
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

            listeners.Add(new Listener($"{name}-listener-{port}", new ListenerArgs
            {
                LoadBalancerArn = loadBalancer.Arn,
                Port = port,
                Protocol = "UDP",
                DefaultActions =
                {
                    new ListenerDefaultActionArgs
                    {
                        Type = "forward",
                        TargetGroupArn = targetGroup.Arn,
                    },
                },
            }, new CustomResourceOptions { Parent = this }));

            targetGroupArns.Add(targetGroup.Arn);
        }

        Listeners = listeners;
        TargetGroupArns = Output.All(targetGroupArns.ToArray())
            .Apply(arns => arns.ToImmutableArray());

        RegisterOutputs(new Dictionary<string, object?>
        {
            ["advertiseAddress"] = AdvertiseAddress,
            ["dnsName"] = DnsName,
            ["targetGroupArns"] = TargetGroupArns,
        });
    }

    /// <summary>
    /// A physical name that is valid, stable and unlikely to collide.
    ///
    /// Load balancer and target group names are capped at 32 characters, may
    /// contain only letters, digits and hyphens, and must be unique per account
    /// and region. Truncating the front of a long prefix is not enough: two
    /// deployments sharing a trailing environment name would collide. So the
    /// name is sanitised and, when it must be shortened, given a short digest of
    /// the full name — deterministic across runs, unlike string.GetHashCode().
    /// </summary>
    public static string PhysicalName(string prefix, string suffix, int maxLength)
    {
        var full = Sanitize($"{prefix}-{suffix}");
        if (full.Length <= maxLength)
        {
            return full;
        }

        var digest = Digest(full, 6);
        var keep = maxLength - digest.Length - 1;
        return Trim($"{full[..keep]}-{digest}");
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.ToLowerInvariant())
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) ? character : '-');
        }
        // Collapse runs so "a__b" does not become "a--b" and waste budget.
        var collapsed = new StringBuilder(builder.Length);
        foreach (var character in builder.ToString())
        {
            if (character != '-' || collapsed.Length == 0 || collapsed[^1] != '-')
            {
                collapsed.Append(character);
            }
        }
        return Trim(collapsed.ToString());
    }

    private static string Trim(string value) => value.Trim('-');

    private static string Digest(string value, int length)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant()[..length];
    }
}
