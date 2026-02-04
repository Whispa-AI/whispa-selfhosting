using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Pulumi;
using Pulumi.Aws;
using Pulumi.Aws.Iam;
using Pulumi.Aws.Lambda;
using Pulumi.Aws.Lambda.Inputs;
using Whispa.Aws.Pulumi.Configuration;

namespace Whispa.Aws.Pulumi.Components;

/// <summary>
/// Creates the AWS Connect integration Lambda function.
/// This Lambda is invoked by AWS Connect Contact Flows to notify Whispa
/// when calls start, enabling real-time transcription via KVS.
/// </summary>
public class LambdaStack : ComponentResource
{
    /// <summary>Lambda function ARN</summary>
    public Output<string> FunctionArn { get; }

    /// <summary>Lambda function name</summary>
    public Output<string> FunctionName { get; }

    public LambdaStack(
        string name,
        WhispaConfig config,
        Output<string> backendUrl,
        Output<string>? apiKeySecretArn = null,
        ComponentResourceOptions? options = null)
        : base("whispa:lambda:LambdaStack", name, options)
    {
        var functionName = config.ResourceName("connect-lambda");

        // Trust policy allowing Lambda and Connect to assume the role
        var assumeRolePolicy = JsonSerializer.Serialize(new
        {
            Version = "2012-10-17",
            Statement = new[]
            {
                new
                {
                    Effect = "Allow",
                    Principal = new
                    {
                        Service = new[] { "lambda.amazonaws.com", "connect.amazonaws.com" }
                    },
                    Action = "sts:AssumeRole",
                },
            },
        });

        // Create IAM role for Lambda execution
        var lambdaRole = new Role($"{name}-role", new RoleArgs
        {
            Name = config.ResourceName("connect-lambda-role"),
            AssumeRolePolicy = assumeRolePolicy,
            Tags = new InputMap<string>
            {
                ["Name"] = config.ResourceName("connect-lambda-role"),
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
            },
        }, new CustomResourceOptions { Parent = this });

        // Attach basic Lambda execution policy (CloudWatch logs)
        new RolePolicyAttachment($"{name}-basic-execution", new RolePolicyAttachmentArgs
        {
            Role = lambdaRole.Name,
            PolicyArn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole",
        }, new CustomResourceOptions { Parent = this });

        // If using Secrets Manager for API key, grant read access
        if (apiKeySecretArn != null)
        {
            var secretsPolicy = apiKeySecretArn.Apply(arn => JsonSerializer.Serialize(new
            {
                Version = "2012-10-17",
                Statement = new[]
                {
                    new
                    {
                        Sid = "ReadApiKeySecret",
                        Effect = "Allow",
                        Action = new[] { "secretsmanager:GetSecretValue" },
                        Resource = arn,
                    },
                },
            }));

            new RolePolicy($"{name}-secrets", new RolePolicyArgs
            {
                Role = lambdaRole.Name,
                Policy = secretsPolicy,
            }, new CustomResourceOptions { Parent = this });
        }

        // Create Lambda deployment package from embedded code
        var lambdaCode = GetLambdaCode();
        var zipPath = CreateDeploymentPackage(lambdaCode);

        // Create Lambda function
        var lambda = new Function($"{name}-function", new FunctionArgs
        {
            Name = functionName,
            Runtime = Runtime.Python3d12,
            Handler = "lambda_function.lambda_handler",
            Role = lambdaRole.Arn,
            Code = new FileArchive(zipPath),
            Timeout = 30,
            MemorySize = 128,
            Environment = new FunctionEnvironmentArgs
            {
                Variables = new InputMap<string>
                {
                    // Backend URL is passed via environment variable
                    // The Lambda will append /awsconnect/call-started
                    ["WHISPA_API_URL"] = backendUrl,
                    // API key can be set via Pulumi config or manually after deployment
                    ["WHISPA_API_KEY"] = config.ConnectApiKey ?? "",
                },
            },
            Tags = new InputMap<string>
            {
                ["Name"] = functionName,
                ["Project"] = config.ProjectName,
                ["Environment"] = config.Environment,
            },
        }, new CustomResourceOptions { Parent = this });

        // Grant AWS Connect permission to invoke this Lambda
        var accountId = Output.Create(GetCallerIdentity.InvokeAsync()).Apply(id => id.AccountId);

        new Permission($"{name}-connect-permission", new PermissionArgs
        {
            Function = lambda.Name,
            Action = "lambda:InvokeFunction",
            Principal = "connect.amazonaws.com",
            SourceAccount = accountId,
            StatementId = "AllowAWSConnect",
        }, new CustomResourceOptions { Parent = this });

        FunctionArn = lambda.Arn;
        FunctionName = lambda.Name;

        // Note: Don't delete the temp zip file here - Pulumi reads it asynchronously.
        // The OS will clean up temp files eventually.

        RegisterOutputs();
    }

    /// <summary>
    /// Returns the Lambda function Python code.
    /// This is embedded directly to avoid file path issues during deployment.
    /// </summary>
    private static string GetLambdaCode() => """"
# AWS Connect Contact Flow Lambda for Whispa Integration.
#
# This Lambda is invoked from an AWS Connect Contact Flow when a call starts.
# It extracts call metadata and forwards it to the Whispa backend to start
# audio capture and transcription.

import json
import os
import urllib.request
import urllib.error

WHISPA_API_URL = os.environ.get("WHISPA_API_URL", "")
WHISPA_API_KEY = os.environ.get("WHISPA_API_KEY", "")


def lambda_handler(event, context):
    """Handle AWS Connect contact flow invocation."""
    print(f"Event received: {json.dumps(event)}")

    # Extract contact data from the event
    details = event.get("Details", {})
    contact_data = details.get("ContactData", {})

    # Core identifiers
    contact_id = contact_data.get("ContactId", "")
    instance_arn = contact_data.get("InstanceARN", "")

    # Customer info
    customer_endpoint = contact_data.get("CustomerEndpoint", {})
    customer_number = customer_endpoint.get("Address", "")

    # Agent info - check multiple sources in order of preference
    agent_data = contact_data.get("Agent", {})
    attributes = contact_data.get("Attributes", {})

    agent_arn = agent_data.get("ARN") or attributes.get("agent_arn") or None
    agent_username = (
        agent_data.get("Username")
        or attributes.get("agent_username")
        or contact_data.get("Name")
        or None
    )

    # Queue info
    queue_data = contact_data.get("Queue", {})
    queue_name = queue_data.get("Name", "")

    # Call direction/initiation method
    initiation_method = contact_data.get("InitiationMethod", "")

    # Media stream info (from "Start media streaming" block)
    media_streams = contact_data.get("MediaStreams", {})
    customer_audio = media_streams.get("Customer", {}).get("Audio", {})
    stream_arn = customer_audio.get("StreamARN", "")

    # Validate required fields
    if not stream_arn:
        print("ERROR: No StreamARN found. Ensure 'Start media streaming' block runs before this Lambda.")
        return {
            "statusCode": 400,
            "error": "Missing stream_arn - 'Start media streaming' block must run first",
        }

    if not contact_id:
        print("ERROR: No ContactId found in event.")
        return {"statusCode": 400, "error": "Missing contact_id in event"}

    # Build payload for Whispa
    payload = {
        "contact_id": contact_id,
        "stream_arn": stream_arn,
        "customer_number": customer_number or None,
        "agent_arn": agent_arn or None,
        "agent_username": agent_username or None,
        "instance_arn": instance_arn or None,
        "queue_name": queue_name or None,
        "initiation_method": initiation_method or None,
    }

    print(f"Payload for Whispa: {json.dumps(payload)}")

    # Forward to Whispa if configured
    if WHISPA_API_URL:
        endpoint = f"{WHISPA_API_URL.rstrip('/')}/awsconnect/call-started"
        headers = {"Content-Type": "application/json"}

        if WHISPA_API_KEY:
            headers["X-API-Key"] = WHISPA_API_KEY

        try:
            req = urllib.request.Request(
                endpoint,
                data=json.dumps(payload).encode("utf-8"),
                headers=headers,
                method="POST",
            )
            with urllib.request.urlopen(req, timeout=10) as resp:
                response_body = resp.read().decode("utf-8")
                print(f"Whispa response: status={resp.status}, body={response_body}")

        except urllib.error.HTTPError as e:
            error_body = e.read().decode("utf-8") if e.fp else ""
            print(f"Whispa HTTP error: status={e.code}, body={error_body}")

        except urllib.error.URLError as e:
            print(f"Whispa connection error: {e.reason}")

        except Exception as e:
            print(f"Whispa unexpected error: {type(e).__name__}: {e}")
    else:
        print("WARNING: WHISPA_API_URL not configured, call will not be captured")

    # Return success to contact flow
    return {
        "statusCode": 200,
        "contactId": contact_id,
        "streamArn": stream_arn,
    }
"""";

    /// <summary>
    /// Creates a temporary zip file containing the Lambda code.
    /// </summary>
    private static string CreateDeploymentPackage(string code)
    {
        var tempDir = Path.GetTempPath();
        var zipPath = Path.Combine(tempDir, $"whispa-connect-lambda-{Guid.NewGuid():N}.zip");

        using var zipArchive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var entry = zipArchive.CreateEntry("lambda_function.py");
        using var writer = new StreamWriter(entry.Open());
        writer.Write(code);

        return zipPath;
    }
}
