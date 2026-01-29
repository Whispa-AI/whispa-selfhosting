using Pulumi;
using Pulumi.Aws.Rds;
using Pulumi.Aws.Rds.Inputs;
using System.Collections.Immutable;
using Whispa.Aws.Pulumi.Configuration;

namespace Whispa.Aws.Pulumi.Components;

/// <summary>
/// Creates RDS PostgreSQL database:
/// - DB subnet group (private subnets)
/// - RDS PostgreSQL 17 instance
/// - Parameter group for PostgreSQL tuning
///
/// The database is placed in private subnets and only accessible from ECS tasks.
/// </summary>
public class DatabaseStack : ComponentResource
{
    /// <summary>Database endpoint hostname</summary>
    public Output<string> DbEndpoint { get; }

    /// <summary>Database port</summary>
    public Output<int> DbPort { get; }

    /// <summary>Full database connection string for asyncpg</summary>
    public Output<string> DatabaseUrl { get; }

    public DatabaseStack(
        string name,
        WhispaConfig config,
        Output<ImmutableArray<string>> privateSubnetIds,
        Output<string> securityGroupId,
        Output<string> dbPassword,
        ComponentResourceOptions? options = null)
        : base("whispa:database:DatabaseStack", name, options)
    {
        // Create DB subnet group using private subnets
        var subnetGroup = new SubnetGroup($"{name}-subnet-group", new SubnetGroupArgs
        {
            SubnetIds = privateSubnetIds,
            Description = "Subnet group for Whispa RDS",
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("db-subnet-group"),
            },
        }, new CustomResourceOptions { Parent = this });

        // Create parameter group for PostgreSQL 17
        var parameterGroup = new ParameterGroup($"{name}-params", new ParameterGroupArgs
        {
            Family = "postgres17",
            Description = "Parameter group for Whispa PostgreSQL",
            Parameters = new[]
            {
                // Log slow queries (for debugging)
                new ParameterGroupParameterArgs
                {
                    Name = "log_min_duration_statement",
                    Value = "1000",  // Log queries taking > 1 second
                },
                // Connection pooling friendly settings
                new ParameterGroupParameterArgs
                {
                    Name = "idle_in_transaction_session_timeout",
                    Value = "60000",  // 60 seconds
                },
            },
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("db-params"),
            },
        }, new CustomResourceOptions { Parent = this });

        // Create RDS PostgreSQL instance
        var dbInstance = new Instance($"{name}-postgres", new InstanceArgs
        {
            // Engine configuration
            Engine = "postgres",
            EngineVersion = "17",
            InstanceClass = config.DbInstanceClass,

            // Storage
            AllocatedStorage = config.DbAllocatedStorage,
            StorageType = "gp3",
            StorageEncrypted = true,

            // Database configuration
            DbName = config.DbName,
            Username = config.DbUsername,
            Password = dbPassword,
            ParameterGroupName = parameterGroup.Name,

            // Network configuration
            DbSubnetGroupName = subnetGroup.Name,
            VpcSecurityGroupIds = new[] { securityGroupId },
            PubliclyAccessible = false,

            // Availability & backup
            MultiAz = config.DbMultiAz,
            BackupRetentionPeriod = config.DbBackupRetentionDays,
            BackupWindow = "03:00-04:00",  // 3-4 AM UTC
            MaintenanceWindow = "Mon:04:00-Mon:05:00",  // Monday 4-5 AM UTC

            // Performance Insights (free tier for 7 days retention)
            PerformanceInsightsEnabled = true,
            PerformanceInsightsRetentionPeriod = 7,

            // Deletion protection - disable for dev, enable for prod
            DeletionProtection = config.Environment == "prod",

            // Skip final snapshot for easier cleanup in dev
            // For prod: final snapshot identifier is static to avoid Pulumi drift on each run
            SkipFinalSnapshot = config.Environment != "prod",
            FinalSnapshotIdentifier = config.Environment == "prod"
                ? config.ResourceName("final-snapshot")
                : null,

            // Auto minor version upgrades
            AutoMinorVersionUpgrade = true,

            // Copy tags to snapshots
            CopyTagsToSnapshot = true,

            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("postgres"),
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
            },
        }, new CustomResourceOptions { Parent = this });

        DbEndpoint = dbInstance.Endpoint.Apply(e => e.Split(':')[0]);  // Remove port from endpoint
        DbPort = Output.Create(5432);

        // Construct asyncpg connection string
        // Format: postgresql+asyncpg://user:password@host:port/database
        DatabaseUrl = Output.All(dbPassword, dbInstance.Endpoint).Apply(values =>
        {
            var password = values[0];
            var endpoint = values[1].Split(':')[0];  // Remove port from endpoint
            return $"postgresql+asyncpg://whispa_admin:{password}@{endpoint}:5432/{config.DbName}";
        });

        RegisterOutputs();
    }
}
