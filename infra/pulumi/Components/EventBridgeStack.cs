using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Pulumi;
using Pulumi.Aws;
using Pulumi.Aws.CloudWatch;
using Pulumi.Aws.CloudWatch.Inputs;
using Pulumi.Aws.Iam;
using Pulumi.Aws.Lambda;
using Pulumi.Aws.Lambda.Inputs;
using Whispa.Aws.Pulumi.Configuration;

namespace Whispa.Aws.Pulumi.Components;

/// <summary>
/// Creates the EventBridge consumer Lambda and rule for forwarding
/// Amazon Connect contact events (agent connect/disconnect, transfers)
/// to the Whispa backend.
/// </summary>
public class EventBridgeStack : ComponentResource
{
    /// <summary>EventBridge consumer Lambda ARN</summary>
    public Output<string> FunctionArn { get; }

    /// <summary>EventBridge consumer Lambda name</summary>
    public Output<string> FunctionName { get; }

    /// <summary>EventBridge rule name</summary>
    public Output<string> RuleName { get; }

    public EventBridgeStack(
        string name,
        WhispaConfig config,
        Output<string> backendUrl,
        Output<string> connectApiKey,
        ComponentResourceOptions? options = null)
        : base("whispa:eventbridge:EventBridgeStack", name, options)
    {
        var functionName = config.ResourceName("eventbridge-consumer");

        // =====================
        // IAM Role for Lambda
        // =====================

        var assumeRolePolicy = JsonSerializer.Serialize(new
        {
            Version = "2012-10-17",
            Statement = new[]
            {
                new
                {
                    Effect = "Allow",
                    Principal = new { Service = "lambda.amazonaws.com" },
                    Action = "sts:AssumeRole",
                },
            },
        });

        var lambdaRole = new Role($"{name}-role", new RoleArgs
        {
            Name = config.ResourceName("eventbridge-consumer-role"),
            AssumeRolePolicy = assumeRolePolicy,
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("eventbridge-consumer-role"),
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
            },
        }, new CustomResourceOptions { Parent = this });

        new RolePolicyAttachment($"{name}-basic-execution", new RolePolicyAttachmentArgs
        {
            Role = lambdaRole.Name,
            PolicyArn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole",
        }, new CustomResourceOptions { Parent = this });

        // =====================
        // Lambda Function
        // =====================

        var lambdaCode = GetLambdaCode();
        var zipPath = CreateDeploymentPackage(lambdaCode);

        // Webhook URL: backend /awsconnect/eventbridge endpoint
        var webhookUrl = backendUrl.Apply(url => $"{url.TrimEnd('/')}/awsconnect/eventbridge");

        var lambda = new Function($"{name}-function", new FunctionArgs
        {
            Name = functionName,
            Runtime = Runtime.NodeJS22dX,
            Handler = "index.handler",
            Role = lambdaRole.Arn,
            Code = new FileArchive(zipPath),
            Timeout = 30,
            MemorySize = 128,
            Environment = new FunctionEnvironmentArgs
            {
                Variables = new InputMap<string>
                {
                    ["WEBHOOK_URL"] = webhookUrl,
                    ["CONNECT_API_KEY"] = connectApiKey,
                },
            },
            Tags = new InputMap<string>
            {
                ["Name"] = functionName,
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
            },
        }, new CustomResourceOptions { Parent = this });

        // =====================
        // EventBridge Rule
        // =====================

        // Construct the Connect instance ARN for filtering
        var accountId = Output.Create(GetCallerIdentity.InvokeAsync()).Apply(id => id.AccountId);
        var instanceArn = Output.Format(
            $"arn:aws:connect:{config.AwsRegion}:{accountId}:instance/{config.ConnectInstanceId}");

        var eventPattern = instanceArn.Apply(arn => JsonSerializer.Serialize(new
        {
            source = new[] { "aws.connect" },
            detail_type = new[] { "Amazon Connect Contact Event" },
            detail = new
            {
                instanceArn = new[] { arn },
            },
        }));

        // EventBridge uses "detail-type" with a hyphen in the JSON pattern,
        // but System.Text.Json serializes "detail_type" with an underscore.
        // Fix by replacing the key after serialization.
        eventPattern = eventPattern.Apply(p => p.Replace("detail_type", "detail-type"));

        var ruleName = config.ResourceName("connect-events");

        var rule = new EventRule($"{name}-rule", new EventRuleArgs
        {
            Name = ruleName,
            Description = "Routes Amazon Connect contact events to Whispa EventBridge consumer Lambda",
            EventPattern = eventPattern,
            Tags = new InputMap<string>
            {
                ["Name"] = ruleName,
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
            },
        }, new CustomResourceOptions { Parent = this });

        // =====================
        // EventBridge Target
        // =====================

        new EventTarget($"{name}-target", new EventTargetArgs
        {
            Rule = rule.Name,
            Arn = lambda.Arn,
            RetryPolicy = new EventTargetRetryPolicyArgs
            {
                MaximumRetryAttempts = 2,
                MaximumEventAgeInSeconds = 300,
            },
        }, new CustomResourceOptions { Parent = this });

        // =====================
        // Lambda Permission
        // =====================

        new Permission($"{name}-eventbridge-permission", new PermissionArgs
        {
            Function = lambda.Name,
            Action = "lambda:InvokeFunction",
            Principal = "events.amazonaws.com",
            SourceArn = rule.Arn,
            StatementId = "AllowEventBridgeInvoke",
        }, new CustomResourceOptions { Parent = this });

        FunctionArn = lambda.Arn;
        FunctionName = lambda.Name;
        RuleName = rule.Name;

        RegisterOutputs();
    }

    /// <summary>
    /// Returns the EventBridge consumer Lambda code (Node.js/ESM).
    /// Forwards Connect contact events to the Whispa backend webhook.
    /// </summary>
    private static string GetLambdaCode() => """
const WEBHOOK_URL = process.env.WEBHOOK_URL;
const CONNECT_API_KEY = process.env.CONNECT_API_KEY;

export const handler = async (event) => {
  try {
    const res = await fetch(WEBHOOK_URL, {
      method: "POST",
      headers: {
        "content-type": "application/json",
        ...(CONNECT_API_KEY ? { "x-api-key": CONNECT_API_KEY } : {}),
      },
      body: JSON.stringify(event),
    });

    const text = await res.text();
    console.log("Forwarded event:", {
      status: res.status,
      ok: res.ok,
      body: text,
    });

    return {
      statusCode: res.ok ? 200 : 502,
      body: JSON.stringify({ ok: res.ok, status: res.status, response: text }),
    };
  } catch (err) {
    console.error("Failed to forward event", err);
    return {
      statusCode: 500,
      body: JSON.stringify({ ok: false, error: String(err) }),
    };
  }
};
""";

    /// <summary>
    /// Creates a temporary zip file containing the Lambda code as index.mjs (ESM).
    /// </summary>
    private static string CreateDeploymentPackage(string code)
    {
        var codeHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(code))).ToLowerInvariant()[..16];

        var tempDir = Path.GetTempPath();
        var zipPath = Path.Combine(tempDir, $"whispa-eventbridge-consumer-{codeHash}.zip");

        if (!File.Exists(zipPath))
        {
            using var zipArchive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            var entry = zipArchive.CreateEntry("index.mjs");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(code);
        }

        return zipPath;
    }
}
