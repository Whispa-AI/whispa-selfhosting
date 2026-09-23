#!/bin/bash
# Wait for an ECS service to finish rolling out a task definition, and fail if it
# does not. Run by `pulumi up` (see ComputeStack.cs) after every service update.
#
# Why this exists: the services run with the deployment circuit breaker and
# automatic rollback. A new task that never turns healthy is rolled back to the
# previous version *after* `pulumi up` has already reported success, so without
# this check a failed release looks like a successful one. (The ECS service's
# own waitForSteadyState does not help on the pinned provider: a rolled-back
# service is "steady" again.)
#
# Usage: wait-for-ecs-rollout.sh <cluster> <service> <expected-task-definition-arn>
# Env:   AWS_REGION / AWS_PROFILE as for the AWS CLI
#        ROLLOUT_TIMEOUT_SECONDS  (default 2400: boot + a full live-call drain)
#        ROLLOUT_POLL_SECONDS     (default 15)

set -euo pipefail

CLUSTER="${1:?cluster name required}"
SERVICE="${2:?service name required}"
EXPECTED="${3:?expected task definition ARN required}"
TIMEOUT="${ROLLOUT_TIMEOUT_SECONDS:-2400}"
POLL="${ROLLOUT_POLL_SECONDS:-15}"

describe() {
    aws ecs describe-services --cluster "$CLUSTER" --services "$SERVICE" "$@"
}

fail() {
    echo "ERROR: $SERVICE: $1" >&2
    echo "Recent service events:" >&2
    describe --query 'services[0].events[:8].[createdAt, message]' --output text >&2 || true
    echo "Check the stopped tasks' logs for the cause. After fixing it, run" >&2
    echo "'pulumi up --refresh' so Pulumi sees the rollback and deploys again." >&2
    exit 1
}

echo "Waiting for $SERVICE to roll out ${EXPECTED##*/} (timeout ${TIMEOUT}s)"
deadline=$((SECONDS + TIMEOUT))
last=""

while true; do
    # One tab-separated line: primary deployment's task definition, rollout
    # state and reason, then the rollout state and reason of the deployment
    # for the expected task definition (None when ECS has already dropped it).
    state=$(describe --output text --query "services[0].[
        deployments[?status=='PRIMARY'] | [0].taskDefinition,
        deployments[?status=='PRIMARY'] | [0].rolloutState,
        deployments[?status=='PRIMARY'] | [0].rolloutStateReason,
        deployments[?taskDefinition=='$EXPECTED'] | [0].rolloutState,
        deployments[?taskDefinition=='$EXPECTED'] | [0].rolloutStateReason]")
    IFS=$'\t' read -r primary_td primary_state primary_reason ours_state ours_reason <<<"$state"

    if [ "$primary_td" != "$EXPECTED" ]; then
        # Rolled back (the circuit breaker makes the previous revision primary
        # again) or superseded by a concurrent deploy. Either way, not ours.
        reason="$ours_reason"
        [ "$reason" = "None" ] && reason="the deployment is no longer present"
        fail "rollout of ${EXPECTED##*/} did not complete; the service is running ${primary_td##*/} (rollout: $ours_state, $reason)"
    fi

    case "$primary_state" in
        COMPLETED)
            echo "$SERVICE: rollout of ${EXPECTED##*/} completed"
            exit 0
            ;;
        FAILED)
            fail "rollout of ${EXPECTED##*/} failed: $primary_reason"
            ;;
    esac

    if [ "$primary_state" != "$last" ]; then
        echo "$SERVICE: $primary_state ($primary_reason)"
        last="$primary_state"
    fi
    if [ "$SECONDS" -ge "$deadline" ]; then
        fail "rollout of ${EXPECTED##*/} still $primary_state after ${TIMEOUT}s"
    fi
    sleep "$POLL"
done
