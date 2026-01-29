using Pulumi;
using Pulumi.Aws.Route53;
using Pulumi.Aws.Route53.Inputs;
using Whispa.Aws.Pulumi.Configuration;

namespace Whispa.Aws.Pulumi.Components;

/// <summary>
/// Creates Route53 DNS records (optional):
/// - A record pointing to ALB (alias)
/// - Optional www CNAME
///
/// Only created if hostedZoneId is provided in config.
/// </summary>
public class DnsStack : ComponentResource
{
    /// <summary>Full frontend domain name with record</summary>
    public Output<string> DomainUrl { get; }

    public DnsStack(
        string name,
        WhispaConfig config,
        Output<string> albDnsName,
        Output<string> albZoneId,
        ComponentResourceOptions? options = null)
        : base("whispa:dns:DnsStack", name, options)
    {
        if (string.IsNullOrEmpty(config.HostedZoneId))
        {
            // No hosted zone provided, skip DNS setup
            DomainUrl = Output.Create($"https://{config.DomainName}");
            RegisterOutputs();
            return;
        }

        // Create A record alias to ALB
        var aRecord = new Record($"{name}-a-record", new RecordArgs
        {
            ZoneId = config.HostedZoneId,
            Name = config.DomainName,
            Type = "A",
            Aliases = new[]
            {
                new RecordAliasArgs
                {
                    Name = albDnsName,
                    ZoneId = albZoneId,
                    EvaluateTargetHealth = true,
                },
            },
        }, new CustomResourceOptions { Parent = this });

        // Create API A record alias if provided
        if (!string.IsNullOrWhiteSpace(config.ApiDomainName) &&
            !string.Equals(config.ApiDomainName, config.DomainName, System.StringComparison.OrdinalIgnoreCase))
        {
            new Record($"{name}-api-record", new RecordArgs
            {
                ZoneId = config.HostedZoneId,
                Name = config.ApiDomainName,
                Type = "A",
                Aliases = new[]
                {
                    new RecordAliasArgs
                    {
                        Name = albDnsName,
                        ZoneId = albZoneId,
                        EvaluateTargetHealth = true,
                    },
                },
            }, new CustomResourceOptions { Parent = this });
        }

        // Create www CNAME if domain doesn't start with www
        if (!config.DomainName.StartsWith("www."))
        {
            new Record($"{name}-www-record", new RecordArgs
            {
                ZoneId = config.HostedZoneId,
                Name = $"www.{config.DomainName}",
                Type = "CNAME",
                Ttl = 300,
                Records = new[] { config.DomainName },
            }, new CustomResourceOptions { Parent = this });
        }

        DomainUrl = Output.Create($"https://{config.DomainName}");

        RegisterOutputs();
    }
}
