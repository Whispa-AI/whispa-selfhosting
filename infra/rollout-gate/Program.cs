// Wait for an ECS service to finish rolling out a task definition, and fail if it
// does not. Run by `pulumi up` (see ComputeStack.cs) after every service update.
//
// Why this exists: the services run with the deployment circuit breaker and
// automatic rollback. A new task that never turns healthy is rolled back to the
// previous version *after* `pulumi up` has already reported success, so without
// this check a failed release looks like a successful one. (The ECS service's own
// waitForSteadyState does not help on the pinned provider: a rolled-back service
// is "steady" again.)
//
// A .NET tool rather than a shell script so it runs wherever Pulumi's .NET program
// does (Linux, macOS, Windows CI) with nothing else installed.
//
// Usage: dotnet run --project infra/rollout-gate -- <cluster> <service> <task-definition-arn>
// Env:   AWS_REGION / AWS_PROFILE as for any AWS SDK
//        ROLLOUT_TIMEOUT_SECONDS  (default 2400: boot + a full live-call drain)
//        ROLLOUT_POLL_SECONDS     (default 15)

using Amazon.ECS;
using Amazon.ECS.Model;
using Task = System.Threading.Tasks.Task;
using Whispa.RolloutGate;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: RolloutGate <cluster> <service> <expected-task-definition-arn>");
    return 2;
}

var (cluster, service, expected) = (args[0], args[1], args[2]);
var timeout = TimeSpan.FromSeconds(ReadSeconds("ROLLOUT_TIMEOUT_SECONDS", 2400));
var poll = TimeSpan.FromSeconds(ReadSeconds("ROLLOUT_POLL_SECONDS", 15));

using var ecs = new AmazonECSClient();
Console.WriteLine($"Waiting for {service} to roll out {RolloutDecision.ShortName(expected)} (timeout {timeout.TotalSeconds:0}s)");

var deadline = DateTime.UtcNow + timeout;
string? lastState = null;
while (true)
{
    var described = await Describe();
    var verdict = RolloutDecision.Evaluate(
        (described.Deployments ?? [])
            .Select(d => new DeploymentSnapshot(
                d.Status ?? "", d.TaskDefinition ?? "", d.RolloutState?.Value, d.RolloutStateReason))
            .ToList(),
        expected);

    switch (verdict)
    {
        case RolloutVerdict.Completed:
            Console.WriteLine($"{service}: rollout of {RolloutDecision.ShortName(expected)} completed");
            return 0;
        case RolloutVerdict.Failed failed:
            return Fail(failed.Reason, described);
        case RolloutVerdict.InProgress progress:
            if (progress.State != lastState)
            {
                Console.WriteLine($"{service}: {progress.State}");
                lastState = progress.State;
            }
            if (DateTime.UtcNow >= deadline)
            {
                return Fail(
                    $"rollout of {RolloutDecision.ShortName(expected)} still {progress.State} after {timeout.TotalSeconds:0}s",
                    described);
            }
            await Task.Delay(poll);
            break;
    }
}

async System.Threading.Tasks.Task<Service> Describe()
{
    var response = await ecs.DescribeServicesAsync(new DescribeServicesRequest
    {
        Cluster = cluster,
        Services = [service],
    });
    return response.Services?.FirstOrDefault()
        ?? throw new InvalidOperationException($"service {service} not found in cluster {cluster}");
}

int Fail(string reason, Service described)
{
    Console.Error.WriteLine($"ERROR: {service}: {reason}");
    Console.Error.WriteLine("Recent service events:");
    foreach (var e in (described.Events ?? []).Take(8))
    {
        Console.Error.WriteLine($"  {e.CreatedAt:u}  {e.Message}");
    }
    Console.Error.WriteLine("Check the stopped tasks' logs for the cause. After fixing it, run");
    Console.Error.WriteLine("'pulumi up --refresh' so Pulumi sees the rollback and deploys again.");
    return 1;
}

static double ReadSeconds(string name, double fallback) =>
    double.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value >= 0 ? value : fallback;
