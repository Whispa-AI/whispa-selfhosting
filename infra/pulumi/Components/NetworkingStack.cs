using Pulumi;
using Pulumi.Aws.Ec2;
using Pulumi.Aws.Ec2.Inputs;
using System.Collections.Immutable;
using Whispa.Aws.Pulumi.Configuration;

namespace Whispa.Aws.Pulumi.Components;

/// <summary>
/// Creates VPC networking infrastructure:
/// - VPC with DNS support
/// - 2 Public subnets (for ALB, NAT Gateway)
/// - 2 Private subnets (for ECS tasks, RDS)
/// - Internet Gateway
/// - NAT Gateway (for private subnet outbound access)
/// - Route tables and associations
///
/// Note: Subnet CIDRs are derived from the VPC CIDR's second octet base.
/// Default VPC CIDR 10.0.0.0/16 creates subnets:
/// - Public: 10.0.1.0/24, 10.0.2.0/24
/// - Private: 10.0.11.0/24, 10.0.12.0/24
/// </summary>
public class NetworkingStack : ComponentResource
{
    /// <summary>VPC ID</summary>
    public Output<string> VpcId { get; }

    /// <summary>Public subnet IDs (for ALB)</summary>
    public Output<ImmutableArray<string>> PublicSubnetIds { get; }

    /// <summary>Private subnet IDs (for ECS, RDS)</summary>
    public Output<ImmutableArray<string>> PrivateSubnetIds { get; }

    /// <summary>Security group for ALB</summary>
    public Output<string> AlbSecurityGroupId { get; }

    /// <summary>Security group for ECS tasks</summary>
    public Output<string> EcsSecurityGroupId { get; }

    /// <summary>Security group for RDS</summary>
    public Output<string> RdsSecurityGroupId { get; }

    public NetworkingStack(string name, WhispaConfig config, ComponentResourceOptions? options = null)
        : base("whispa:networking:NetworkingStack", name, options)
    {
        var awsRegion = config.AwsRegion;

        // Get availability zones for the region (require at least 2)
        var azs = global::Pulumi.Aws.GetAvailabilityZones.Invoke(new()
        {
            State = "available",
        }).Apply(result =>
        {
            var distinct = result.Names.Distinct().ToArray();
            if (distinct.Length < 2)
            {
                throw new Exception(
                    $"RDS requires at least 2 availability zones, but only {distinct.Length} were found."
                );
            }

            return distinct.Take(2).ToArray();
        });

        var az1 = azs.Apply(names =>
        {
            if (names.Length < 1)
            {
                throw new Exception("No availability zones returned for subnet creation.");
            }

            return names[0];
        });

        var az2 = azs.Apply(names =>
        {
            if (names.Length < 2)
            {
                throw new Exception("Only one availability zone returned; need at least two.");
            }

            return names[1];
        });

        // Create VPC
        var vpc = new Vpc($"{name}-vpc", new VpcArgs
        {
            CidrBlock = config.VpcCidr,
            EnableDnsHostnames = true,  // Required for RDS endpoint resolution
            EnableDnsSupport = true,
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("vpc"),
            },
        }, new CustomResourceOptions { Parent = this });

        VpcId = vpc.Id;

        // Create Internet Gateway
        var igw = new InternetGateway($"{name}-igw", new InternetGatewayArgs
        {
            VpcId = vpc.Id,
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("igw"),
            },
        }, new CustomResourceOptions { Parent = this });

        // Create public subnets (in first 2 AZs)
        var publicSubnets = new List<Subnet>();
        var privateSubnets = new List<Subnet>();

        // Parse VPC CIDR to derive subnet CIDRs (e.g., 10.0.0.0/16 -> base "10.0")
        var cidrParts = config.VpcCidr.Split('/');
        if (cidrParts.Length != 2 || !int.TryParse(cidrParts[1], out var prefix))
        {
            throw new ArgumentException($"Invalid VPC CIDR: {config.VpcCidr}");
        }

        if (prefix > 20)
        {
            throw new ArgumentException(
                $"VPC CIDR {config.VpcCidr} is too small for the default subnet layout. " +
                "Use a /20 or larger (e.g., /16)."
            );
        }

        var vpcCidrParts = cidrParts[0].Split('.');
        if (vpcCidrParts.Length != 4)
        {
            throw new ArgumentException($"Invalid VPC CIDR: {config.VpcCidr}");
        }

        var cidrBase = $"{vpcCidrParts[0]}.{vpcCidrParts[1]}";

        for (int i = 0; i < 2; i++)
        {
            var az = i == 0 ? az1 : az2;

            // Public subnet: {base}.1.0/24, {base}.2.0/24
            var publicSubnet = new Subnet($"{name}-public-{i + 1}", new SubnetArgs
            {
                VpcId = vpc.Id,
                CidrBlock = $"{cidrBase}.{i + 1}.0/24",
                AvailabilityZone = az,
                MapPublicIpOnLaunch = true,
                Tags = new InputMap<string>
                {
                    ["Name"] = config.ResourceName($"public-{i + 1}"),
                    ["Type"] = "public",
                },
            }, new CustomResourceOptions
            {
                Parent = this,
                DeleteBeforeReplace = true,
            });
            publicSubnets.Add(publicSubnet);

            // Private subnet: {base}.11.0/24, {base}.12.0/24
            var privateSubnet = new Subnet($"{name}-private-{i + 1}", new SubnetArgs
            {
                VpcId = vpc.Id,
                CidrBlock = $"{cidrBase}.{i + 11}.0/24",
                AvailabilityZone = az,
                MapPublicIpOnLaunch = false,
                Tags = new InputMap<string>
                {
                    ["Name"] = config.ResourceName($"private-{i + 1}"),
                    ["Type"] = "private",
                },
            }, new CustomResourceOptions
            {
                Parent = this,
                DeleteBeforeReplace = true,
            });
            privateSubnets.Add(privateSubnet);
        }

        PublicSubnetIds = Output.All(publicSubnets.Select(s => s.Id)).Apply(ids => ids.ToImmutableArray());
        PrivateSubnetIds = Output.All(privateSubnets.Select(s => s.Id)).Apply(ids => ids.ToImmutableArray());

        // Create Elastic IP for NAT Gateway
        var natEip = new Eip($"{name}-nat-eip", new EipArgs
        {
            Domain = "vpc",
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("nat-eip"),
            },
        }, new CustomResourceOptions { Parent = this });

        // Create NAT Gateway in first public subnet
        var natGateway = new NatGateway($"{name}-nat", new NatGatewayArgs
        {
            AllocationId = natEip.Id,
            SubnetId = publicSubnets[0].Id,
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("nat"),
            },
        }, new CustomResourceOptions { Parent = this, DependsOn = { igw } });

        // Create public route table
        var publicRouteTable = new RouteTable($"{name}-public-rt", new RouteTableArgs
        {
            VpcId = vpc.Id,
            Routes = new[]
            {
                new RouteTableRouteArgs
                {
                    CidrBlock = "0.0.0.0/0",
                    GatewayId = igw.Id,
                },
            },
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("public-rt"),
            },
        }, new CustomResourceOptions { Parent = this });

        // Create private route table
        var privateRouteTable = new RouteTable($"{name}-private-rt", new RouteTableArgs
        {
            VpcId = vpc.Id,
            Routes = new[]
            {
                new RouteTableRouteArgs
                {
                    CidrBlock = "0.0.0.0/0",
                    NatGatewayId = natGateway.Id,
                },
            },
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("private-rt"),
            },
        }, new CustomResourceOptions { Parent = this });

        // Associate route tables with subnets
        for (int i = 0; i < 2; i++)
        {
            new RouteTableAssociation($"{name}-public-rta-{i + 1}", new RouteTableAssociationArgs
            {
                SubnetId = publicSubnets[i].Id,
                RouteTableId = publicRouteTable.Id,
            }, new CustomResourceOptions { Parent = this });

            new RouteTableAssociation($"{name}-private-rta-{i + 1}", new RouteTableAssociationArgs
            {
                SubnetId = privateSubnets[i].Id,
                RouteTableId = privateRouteTable.Id,
            }, new CustomResourceOptions { Parent = this });
        }

        // ===================
        // Security Groups
        // ===================

        // ALB Security Group - allows HTTP/HTTPS from internet
        var albSg = new SecurityGroup($"{name}-alb-sg", new SecurityGroupArgs
        {
            VpcId = vpc.Id,
            Description = "Security group for Application Load Balancer",
            Ingress = new[]
            {
                new SecurityGroupIngressArgs
                {
                    Protocol = "tcp",
                    FromPort = 80,
                    ToPort = 80,
                    CidrBlocks = new[] { "0.0.0.0/0" },
                    Description = "HTTP from internet",
                },
                new SecurityGroupIngressArgs
                {
                    Protocol = "tcp",
                    FromPort = 443,
                    ToPort = 443,
                    CidrBlocks = new[] { "0.0.0.0/0" },
                    Description = "HTTPS from internet",
                },
            },
            Egress = new[]
            {
                new SecurityGroupEgressArgs
                {
                    Protocol = "-1",
                    FromPort = 0,
                    ToPort = 0,
                    CidrBlocks = new[] { "0.0.0.0/0" },
                    Description = "Allow all outbound",
                },
            },
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("alb-sg"),
            },
        }, new CustomResourceOptions { Parent = this });

        AlbSecurityGroupId = albSg.Id;

        // ECS Security Group - allows traffic from ALB
        var ecsSg = new SecurityGroup($"{name}-ecs-sg", new SecurityGroupArgs
        {
            VpcId = vpc.Id,
            Description = "Security group for ECS tasks",
            Ingress = new[]
            {
                new SecurityGroupIngressArgs
                {
                    Protocol = "tcp",
                    FromPort = 8000,
                    ToPort = 8000,
                    SecurityGroups = new[] { albSg.Id },
                    Description = "Backend from ALB",
                },
                new SecurityGroupIngressArgs
                {
                    Protocol = "tcp",
                    FromPort = 3000,
                    ToPort = 3000,
                    SecurityGroups = new[] { albSg.Id },
                    Description = "Frontend from ALB",
                },
            },
            Egress = new[]
            {
                new SecurityGroupEgressArgs
                {
                    Protocol = "-1",
                    FromPort = 0,
                    ToPort = 0,
                    CidrBlocks = new[] { "0.0.0.0/0" },
                    Description = "Allow all outbound (for pulling images, API calls)",
                },
            },
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("ecs-sg"),
            },
        }, new CustomResourceOptions { Parent = this });

        EcsSecurityGroupId = ecsSg.Id;

        // RDS Security Group - allows traffic from ECS tasks only
        var rdsSg = new SecurityGroup($"{name}-rds-sg", new SecurityGroupArgs
        {
            VpcId = vpc.Id,
            Description = "Security group for RDS PostgreSQL",
            Ingress = new[]
            {
                new SecurityGroupIngressArgs
                {
                    Protocol = "tcp",
                    FromPort = 5432,
                    ToPort = 5432,
                    SecurityGroups = new[] { ecsSg.Id },
                    Description = "PostgreSQL from ECS tasks",
                },
            },
            Egress = new[]
            {
                new SecurityGroupEgressArgs
                {
                    Protocol = "-1",
                    FromPort = 0,
                    ToPort = 0,
                    CidrBlocks = new[] { "0.0.0.0/0" },
                    Description = "Allow all outbound",
                },
            },
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("rds-sg"),
            },
        }, new CustomResourceOptions { Parent = this });

        RdsSecurityGroupId = rdsSg.Id;

        RegisterOutputs();
    }
}
