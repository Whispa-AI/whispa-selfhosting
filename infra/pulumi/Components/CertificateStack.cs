using System;
using System.Collections.Generic;
using System.Linq;
using Pulumi;
using Pulumi.Aws.Acm;
using Pulumi.Aws.Route53;
using Whispa.Aws.Pulumi.Configuration;

namespace Whispa.Aws.Pulumi.Components;

/// <summary>
/// Requests and validates an ACM certificate via Route53 DNS records.
/// Requires hostedZoneId to be set.
/// </summary>
public class CertificateStack : ComponentResource
{
    public Output<string> CertificateArn { get; }

    public CertificateStack(
        string name,
        WhispaConfig config,
        string hostedZoneId,
        ComponentResourceOptions? options = null)
        : base("whispa:cert:CertificateStack", name, options)
    {
        if (string.IsNullOrWhiteSpace(hostedZoneId))
        {
            throw new InvalidOperationException("hostedZoneId is required to auto-manage certificates.");
        }

        var altNames = new List<string>();
        if (!string.IsNullOrWhiteSpace(config.ApiDomainName) &&
            !string.Equals(config.ApiDomainName, config.DomainName, StringComparison.OrdinalIgnoreCase))
        {
            altNames.Add(config.ApiDomainName);
        }

        var cert = new Certificate($"{name}-cert", new CertificateArgs
        {
            DomainName = config.DomainName,
            SubjectAlternativeNames = altNames.Count > 0 ? altNames : null,
            ValidationMethod = "DNS",
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("acm-cert"),
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
            },
        }, new CustomResourceOptions { Parent = this });

        var validationRecordFqdns = cert.DomainValidationOptions.Apply(options =>
        {
            var fqdns = new List<string>();
            for (var i = 0; i < options.Length; i++)
            {
                var option = options[i];
                var record = new Record($"{name}-cert-validation-{i}", new RecordArgs
                {
                    ZoneId = hostedZoneId,
                    Name = option.ResourceRecordName,
                    Type = option.ResourceRecordType,
                    Records = new[] { option.ResourceRecordValue },
                    Ttl = 300,
                }, new CustomResourceOptions { Parent = this });

                fqdns.Add(option.ResourceRecordName);
            }

            return fqdns.ToArray();
        });

        var validation = new CertificateValidation($"{name}-cert-validation", new CertificateValidationArgs
        {
            CertificateArn = cert.Arn,
            ValidationRecordFqdns = validationRecordFqdns,
        }, new CustomResourceOptions { Parent = this });

        CertificateArn = validation.CertificateArn;

        RegisterOutputs();
    }
}
