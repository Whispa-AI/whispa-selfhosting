using Pulumi;
using Pulumi.Aws.CloudWatch;

namespace Whispa.Aws.Pulumi.Components;

/// <summary>
/// CloudWatch metric alarms that notify the alert topic, shared by every
/// component that alarms on an AWS metric.
/// </summary>
internal static class MetricAlarms
{
    /// <summary>
    /// Creates an alarm over 5-minute periods that fires once every one of the
    /// last <paramref name="evaluationPeriods"/> periods breaches. Pass a
    /// percentile such as "p90" as <paramref name="statistic"/> to alarm on it.
    /// </summary>
    public static MetricAlarm Create(
        Resource parent,
        string resourceName,
        Input<string> alarmName,
        string metricNamespace,
        string metricName,
        InputMap<string> dimensions,
        double threshold,
        Input<string> description,
        InputMap<string> tags,
        Input<string> alarmTopicArn,
        string comparisonOperator = "GreaterThanThreshold",
        string statistic = "Average",
        int evaluationPeriods = 2,
        string? treatMissingData = null)
    {
        var args = new MetricAlarmArgs
        {
            Name = alarmName,
            ComparisonOperator = comparisonOperator,
            EvaluationPeriods = evaluationPeriods,
            MetricName = metricName,
            Namespace = metricNamespace,
            Period = 300,
            Threshold = threshold,
            AlarmDescription = description,
            AlarmActions = new InputList<string> { alarmTopicArn },
            Dimensions = dimensions,
            Tags = tags,
        };

        // Percentiles go in a separate field; CloudWatch rejects an alarm that sets both.
        if (statistic.StartsWith('p'))
            args.ExtendedStatistic = statistic;
        else
            args.Statistic = statistic;

        if (treatMissingData is not null)
            args.TreatMissingData = treatMissingData;

        return new MetricAlarm(resourceName, args, new CustomResourceOptions { Parent = parent });
    }
}
