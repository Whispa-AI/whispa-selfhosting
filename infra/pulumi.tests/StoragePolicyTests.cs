using System.Text.Json;
using Whispa.Aws.Pulumi.Components;
using Xunit;

namespace Whispa.Aws.Pulumi.Tests;

/// <summary>
/// Pins the audio bucket policies. A typo in a condition key or operator makes
/// IAM ignore the statement silently: the bucket would accept plain HTTP, or
/// replication would stall with access denied.
/// </summary>
public class StoragePolicyTests
{
    private const string Source = "arn:aws:s3:::whispa-prod-audio-20260101000000000000000001";
    private const string Replica = "arn:aws:s3:::whispa-prod-audio-replica-20260101000000000000000002";

    [Fact]
    public void HttpsOnlyPolicy_Denies_Every_Insecure_Request_On_Bucket_And_Objects()
    {
        var statement = JsonDocument.Parse(StorageStack.HttpsOnlyPolicy(Source))
            .RootElement.GetProperty("Statement").EnumerateArray().Single();

        Assert.Equal("Deny", statement.GetProperty("Effect").GetString());
        Assert.Equal("*", statement.GetProperty("Principal").GetString());
        Assert.Equal("s3:*", statement.GetProperty("Action").GetString());
        Assert.Equal(
            new[] { Source, $"{Source}/*" },
            statement.GetProperty("Resource").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(
            "false",
            statement.GetProperty("Condition").GetProperty("Bool").GetProperty("aws:SecureTransport").GetString());
    }

    [Fact]
    public void ReplicationPolicy_Reads_The_Source_And_Writes_Only_The_Replica()
    {
        var statements = JsonDocument.Parse(StorageStack.ReplicationPolicy(Source, Replica))
            .RootElement.GetProperty("Statement").EnumerateArray()
            .ToDictionary(s => s.GetProperty("Resource").GetString()!, s => s.GetProperty("Action")
                .EnumerateArray().Select(a => a.GetString()!).ToArray());

        Assert.Contains("s3:GetReplicationConfiguration", statements[Source]);
        Assert.Contains("s3:GetObjectVersionForReplication", statements[$"{Source}/*"]);
        Assert.Equal(
            new[] { "s3:ReplicateObject", "s3:ReplicateDelete", "s3:ReplicateTags" },
            statements[$"{Replica}/*"]);
    }
}
