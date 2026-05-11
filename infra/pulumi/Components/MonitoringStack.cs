using Pulumi;
using Pulumi.Aws.CloudWatch;
using Pulumi.Aws.Sns;
using Whispa.Aws.Pulumi.Configuration;

namespace Whispa.Aws.Pulumi.Components;

/// <summary>
/// Creates CloudWatch monitoring infrastructure:
/// - Log groups for ECS containers
/// - Log retention policies
/// - Optional RDS I/O alarms
/// </summary>
public class MonitoringStack : ComponentResource
{
    /// <summary>Backend log group name</summary>
    public Output<string> BackendLogGroupName { get; }

    /// <summary>Frontend log group name</summary>
    public Output<string> FrontendLogGroupName { get; }

    /// <summary>Backend log group ARN</summary>
    public Output<string> BackendLogGroupArn { get; }

    /// <summary>Frontend log group ARN</summary>
    public Output<string> FrontendLogGroupArn { get; }

    public MonitoringStack(
        string name,
        WhispaConfig config,
        Input<string> managedDbInstanceIdentifier,
        ComponentResourceOptions? options = null)
        : base("whispa:monitoring:MonitoringStack", name, options)
    {
        // Retention period based on environment
        var retentionDays = config.Environment == "prod" ? 30 : 7;

        // Backend log group
        var backendLogGroup = new LogGroup($"{name}-backend-logs", new LogGroupArgs
        {
            Name = $"/ecs/{config.ProjectName}/{config.Environment}/backend",
            RetentionInDays = retentionDays,
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("backend-logs"),
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
                ["Service"] = "backend",
            },
        }, new CustomResourceOptions { Parent = this });

        BackendLogGroupName = backendLogGroup.Name;
        BackendLogGroupArn = backendLogGroup.Arn;

        // Frontend log group
        var frontendLogGroup = new LogGroup($"{name}-frontend-logs", new LogGroupArgs
        {
            Name = $"/ecs/{config.ProjectName}/{config.Environment}/frontend",
            RetentionInDays = retentionDays,
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("frontend-logs"),
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
                ["Service"] = "frontend",
            },
        }, new CustomResourceOptions { Parent = this });

        FrontendLogGroupName = frontendLogGroup.Name;
        FrontendLogGroupArn = frontendLogGroup.Arn;

        ConfigureRdsIoAlarms(name, config, managedDbInstanceIdentifier);

        RegisterOutputs();
    }

    private void ConfigureRdsIoAlarms(string name, WhispaConfig config, Input<string> managedDbInstanceIdentifier)
    {
        if (!config.EnableRdsIoAlarms)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(config.AlarmSnsTopicArn) && string.IsNullOrWhiteSpace(config.AlarmEmailAddress))
        {
            throw new InvalidOperationException(
                "enableRdsIoAlarms requires either alarmSnsTopicArn or alarmEmailAddress to be configured.");
        }

        Output<string> alarmTopicArn;

        if (!string.IsNullOrWhiteSpace(config.AlarmSnsTopicArn))
        {
            alarmTopicArn = Output.Create(config.AlarmSnsTopicArn);
        }
        else
        {
            var alarmTopic = new Topic($"{name}-rds-io-alarms", new TopicArgs
            {
                Name = config.ResourceName("rds-io-alarms"),
                Tags = new InputMap<string>
                {
                    ["Name"] = config.ResourceName("rds-io-alarms"),
                    ["Project"] = config.ProjectName,
                    ["Environment"] = config.Environment,
                    ["Purpose"] = "RDS I/O Monitoring",
                },
            }, new CustomResourceOptions { Parent = this });

            alarmTopicArn = alarmTopic.Arn;
        }

        if (!string.IsNullOrWhiteSpace(config.AlarmEmailAddress))
        {
            _ = new TopicSubscription($"{name}-rds-io-alarms-email", new TopicSubscriptionArgs
            {
                Topic = alarmTopicArn,
                Protocol = "email",
                Endpoint = config.AlarmEmailAddress,
            }, new CustomResourceOptions { Parent = this });
        }

        CreateInstanceIoAlarms(
            resourceNamePrefix: $"{name}-managed-rds",
            identifier: managedDbInstanceIdentifier,
            alarmTopicArn: alarmTopicArn,
            config: config);
        CreateInstanceCapacityAlarms(
            resourceNamePrefix: $"{name}-managed-rds",
            identifier: managedDbInstanceIdentifier,
            alarmTopicArn: alarmTopicArn,
            config: config);

        foreach (var instanceIdentifier in config.RdsInstanceIdentifiers)
        {
            CreateInstanceIoAlarms(
                resourceNamePrefix: $"{name}-{instanceIdentifier}-rds",
                identifier: instanceIdentifier,
                alarmTopicArn: alarmTopicArn,
                config: config);
            CreateInstanceCapacityAlarms(
                resourceNamePrefix: $"{name}-{instanceIdentifier}-rds",
                identifier: instanceIdentifier,
                alarmTopicArn: alarmTopicArn,
                config: config);
        }

        foreach (var clusterIdentifier in config.RdsClusterIdentifiers)
        {
            CreateClusterIoAlarms(
                resourceNamePrefix: $"{name}-{clusterIdentifier}-rds-cluster",
                identifier: clusterIdentifier,
                alarmTopicArn: alarmTopicArn,
                config: config);
            CreateClusterCapacityAlarms(
                resourceNamePrefix: $"{name}-{clusterIdentifier}-rds-cluster",
                identifier: clusterIdentifier,
                alarmTopicArn: alarmTopicArn,
                config: config);
        }

        foreach (var clusterIdentifier in config.AuroraMySqlClusterIdentifiers)
        {
            CreateAuroraMySqlStorageAlarms(
                resourceNamePrefix: $"{name}-{clusterIdentifier}-aurora-mysql-cluster",
                identifier: clusterIdentifier,
                alarmTopicArn: alarmTopicArn,
                config: config);
        }
    }

    private void CreateInstanceIoAlarms(
        string resourceNamePrefix,
        Input<string> identifier,
        Input<string> alarmTopicArn,
        WhispaConfig config)
    {
        CreateMetricAlarm(
            $"{resourceNamePrefix}-disk-queue-depth",
            identifier.Apply(id => $"{id}-disk-queue-depth-high"),
            "DiskQueueDepth",
            config.RdsDiskQueueDepthAlarmThreshold,
            identifier.Apply(id => $"This metric monitors RDS disk queue depth for {id}"),
            "DBInstanceIdentifier",
            identifier,
            new InputMap<string>
            {
                ["Metric"] = "DiskQueueDepth",
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
                ["Instance"] = identifier,
            },
            alarmTopicArn);

        CreateMetricAlarm(
            $"{resourceNamePrefix}-read-iops",
            identifier.Apply(id => $"{id}-read-iops-high"),
            "ReadIOPS",
            config.RdsReadIopsAlarmThreshold,
            identifier.Apply(id => $"This metric monitors RDS read IOPS for {id}"),
            "DBInstanceIdentifier",
            identifier,
            new InputMap<string>
            {
                ["Metric"] = "ReadIOPS",
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
                ["Instance"] = identifier,
            },
            alarmTopicArn);

        CreateMetricAlarm(
            $"{resourceNamePrefix}-write-iops",
            identifier.Apply(id => $"{id}-write-iops-high"),
            "WriteIOPS",
            config.RdsWriteIopsAlarmThreshold,
            identifier.Apply(id => $"This metric monitors RDS write IOPS for {id}"),
            "DBInstanceIdentifier",
            identifier,
            new InputMap<string>
            {
                ["Metric"] = "WriteIOPS",
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
                ["Instance"] = identifier,
            },
            alarmTopicArn);
    }

    private void CreateInstanceCapacityAlarms(
        string resourceNamePrefix,
        Input<string> identifier,
        Input<string> alarmTopicArn,
        WhispaConfig config)
    {
        CreateMetricAlarm(
            $"{resourceNamePrefix}-free-storage-space",
            identifier.Apply(id => $"{id}-free-storage-space-low"),
            "FreeStorageSpace",
            config.RdsFreeStorageSpaceAlarmThreshold,
            identifier.Apply(id => $"This metric monitors low free storage space for RDS instance {id}"),
            "DBInstanceIdentifier",
            identifier,
            new InputMap<string>
            {
                ["Metric"] = "FreeStorageSpace",
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
                ["Instance"] = identifier,
            },
            alarmTopicArn,
            comparisonOperator: "LessThanThreshold",
            statistic: "Minimum");

        CreateMetricAlarm(
            $"{resourceNamePrefix}-freeable-memory",
            identifier.Apply(id => $"{id}-freeable-memory-low"),
            "FreeableMemory",
            config.RdsFreeableMemoryAlarmThreshold,
            identifier.Apply(id => $"This metric monitors low freeable memory for RDS instance {id}"),
            "DBInstanceIdentifier",
            identifier,
            new InputMap<string>
            {
                ["Metric"] = "FreeableMemory",
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
                ["Instance"] = identifier,
            },
            alarmTopicArn,
            comparisonOperator: "LessThanThreshold",
            statistic: "Minimum");

        CreateMetricAlarm(
            $"{resourceNamePrefix}-cpu-utilization",
            identifier.Apply(id => $"{id}-cpu-utilization-high"),
            "CPUUtilization",
            config.RdsCpuUtilizationAlarmThreshold,
            identifier.Apply(id => $"This metric monitors high CPU utilization for RDS instance {id}"),
            "DBInstanceIdentifier",
            identifier,
            new InputMap<string>
            {
                ["Metric"] = "CPUUtilization",
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
                ["Instance"] = identifier,
            },
            alarmTopicArn,
            comparisonOperator: "GreaterThanThreshold",
            statistic: "Average");
    }

    private void CreateClusterIoAlarms(
        string resourceNamePrefix,
        string identifier,
        Input<string> alarmTopicArn,
        WhispaConfig config)
    {
        CreateMetricAlarm(
            $"{resourceNamePrefix}-disk-queue-depth",
            $"{identifier}-cluster-disk-queue-depth-high",
            "DiskQueueDepth",
            config.RdsDiskQueueDepthAlarmThreshold,
            $"This metric monitors RDS cluster disk queue depth for {identifier}",
            "DBClusterIdentifier",
            identifier,
            new InputMap<string>
            {
                ["Metric"] = "DiskQueueDepth",
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
                ["Cluster"] = identifier,
            },
            alarmTopicArn);

        CreateMetricAlarm(
            $"{resourceNamePrefix}-volume-read-iops",
            $"{identifier}-cluster-volume-read-iops-high",
            "VolumeReadIOPs",
            config.RdsVolumeReadIopsAlarmThreshold,
            $"This metric monitors RDS cluster volume read IOPS for {identifier}",
            "DBClusterIdentifier",
            identifier,
            new InputMap<string>
            {
                ["Metric"] = "VolumeReadIOPs",
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
                ["Cluster"] = identifier,
            },
            alarmTopicArn);

        CreateMetricAlarm(
            $"{resourceNamePrefix}-volume-write-iops",
            $"{identifier}-cluster-volume-write-iops-high",
            "VolumeWriteIOPS",
            config.RdsVolumeWriteIopsAlarmThreshold,
            $"This metric monitors RDS cluster volume write IOPS for {identifier}",
            "DBClusterIdentifier",
            identifier,
            new InputMap<string>
            {
                ["Metric"] = "VolumeWriteIOPS",
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
                ["Cluster"] = identifier,
            },
            alarmTopicArn);
    }

    private void CreateClusterCapacityAlarms(
        string resourceNamePrefix,
        string identifier,
        Input<string> alarmTopicArn,
        WhispaConfig config)
    {
        CreateMetricAlarm(
            $"{resourceNamePrefix}-free-local-storage",
            $"{identifier}-cluster-free-local-storage-low",
            "FreeLocalStorage",
            config.RdsFreeLocalStorageAlarmThreshold,
            $"This metric monitors low free local storage for RDS cluster {identifier}",
            "DBClusterIdentifier",
            identifier,
            new InputMap<string>
            {
                ["Metric"] = "FreeLocalStorage",
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
                ["Cluster"] = identifier,
            },
            alarmTopicArn,
            comparisonOperator: "LessThanThreshold",
            statistic: "Minimum");
    }

    private void CreateAuroraMySqlStorageAlarms(
        string resourceNamePrefix,
        string identifier,
        Input<string> alarmTopicArn,
        WhispaConfig config)
    {
        CreateMetricAlarm(
            $"{resourceNamePrefix}-aurora-volume-bytes-left-total",
            $"{identifier}-cluster-aurora-volume-bytes-left-total-low",
            "AuroraVolumeBytesLeftTotal",
            config.AuroraVolumeBytesLeftTotalAlarmThreshold,
            $"This metric monitors low Aurora volume bytes left for cluster {identifier}",
            "DBClusterIdentifier",
            identifier,
            new InputMap<string>
            {
                ["Metric"] = "AuroraVolumeBytesLeftTotal",
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
                ["Cluster"] = identifier,
            },
            alarmTopicArn,
            comparisonOperator: "LessThanThreshold",
            statistic: "Minimum");
    }

    private void CreateMetricAlarm(
        string resourceName,
        Input<string> alarmName,
        string metricName,
        double threshold,
        Input<string> description,
        string dimensionName,
        Input<string> dimensionValue,
        InputMap<string> tags,
        Input<string> alarmTopicArn,
        string comparisonOperator = "GreaterThanThreshold",
        string statistic = "Average")
    {
        _ = new MetricAlarm(resourceName, new MetricAlarmArgs
        {
            Name = alarmName,
            ComparisonOperator = comparisonOperator,
            EvaluationPeriods = 2,
            MetricName = metricName,
            Namespace = "AWS/RDS",
            Period = 300,
            Statistic = statistic,
            Threshold = threshold,
            AlarmDescription = description,
            AlarmActions = new InputList<string> { alarmTopicArn },
            Dimensions = new InputMap<string>
            {
                [dimensionName] = dimensionValue,
            },
            Tags = tags,
        }, new CustomResourceOptions { Parent = this });
    }
}
