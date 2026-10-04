#!/usr/bin/env bash
# ECS health gate of the AWS deploy pipeline (T146).
# Usage: ecs-health.sh <region> <cluster> <service>=<applied-task-definition-arn>...
# 1. waits (aws ecs wait services-stable, retried ATTEMPTS times: the waiter gives up after 10
#    minutes) until the services are stable;
# 2. then checks, per service, what `services-stable` cannot tell: with the deployment circuit
#    breaker's rollback enabled, a failed deployment is rolled back to the previous task definition
#    and the services still become "stable". The gate fails unless
#      - the PRIMARY deployment's rolloutState is COMPLETED,
#      - its taskDefinition is exactly the ARN Terraform applied, and
#      - no deployment of the service is FAILED.
# Prints the service events when it fails. Needs the AWS CLI with credentials for the region.
# Tests: bash .github/scripts/ecs-health.test.sh (aws is a stub there).
set -uo pipefail

attempts="${ATTEMPTS:-3}"

if [ "$#" -lt 3 ]; then
  echo "usage: $0 <region> <cluster> <service>=<task-definition-arn>..." >&2
  exit 2
fi
region="$1"
cluster="$2"
shift 2

declare -A expected
services=()
for pair in "$@"; do
  name="${pair%%=*}"
  arn="${pair#*=}"
  if [ -z "$name" ] || [ -z "$arn" ] || [ "$name" = "$pair" ]; then
    echo "::error::Invalid <service>=<task-definition-arn> argument: '$pair'" >&2
    exit 2
  fi
  expected[$name]="$arn"
  services+=("$name")
done

print_events() {
  aws ecs describe-services --region "$region" --cluster "$cluster" --services "${services[@]}" \
    --query 'services[].{service:serviceName,desired:desiredCount,running:runningCount,pending:pendingCount,events:events[0:5].message}' \
    --output json || true
}

stable=0
for attempt in $(seq 1 "$attempts"); do
  echo "Waiting for ${services[*]} in $cluster to be stable (attempt $attempt/$attempts)..."
  if aws ecs wait services-stable --region "$region" --cluster "$cluster" --services "${services[@]}"; then
    stable=1
    break
  fi
done
if [ "$stable" -ne 1 ]; then
  echo "::error::ECS services in $cluster did not become stable." >&2
  echo "ECS services in $cluster did not become stable." >&2
  print_events
  exit 1
fi

# One tab-separated row per service: name, PRIMARY task definition, PRIMARY rolloutState, number of
# FAILED deployments.
if ! report="$(aws ecs describe-services --region "$region" --cluster "$cluster" --services "${services[@]}" \
  --query "services[].[serviceName, deployments[?status=='PRIMARY'] | [0].taskDefinition, deployments[?status=='PRIMARY'] | [0].rolloutState, length(deployments[?rolloutState=='FAILED'])]" \
  --output text)"; then
  echo "::error::Could not read the ECS services of $cluster." >&2
  echo "Could not read the ECS services of $cluster." >&2
  exit 1
fi

declare -A seen
problems=()
while IFS=$'\t' read -r name task_definition rollout failed_count; do
  [ -z "$name" ] && continue
  seen[$name]=1
  want="${expected[$name]:-}"
  if [ -z "$want" ]; then
    continue
  fi
  if [ "$rollout" != "COMPLETED" ]; then
    problems+=("$name: PRIMARY deployment rolloutState is ${rollout:-unknown}, not COMPLETED")
  fi
  if [ "$task_definition" != "$want" ]; then
    problems+=("$name: runs $task_definition but Terraform applied $want (rolled back by the circuit breaker?)")
  fi
  if [ "${failed_count:-0}" != "0" ]; then
    problems+=("$name: ${failed_count} deployment(s) FAILED")
  fi
  echo "$name: rollout ${rollout:-unknown}, task definition ${task_definition:-unknown}"
done <<< "$report"

for name in "${services[@]}"; do
  [ -n "${seen[$name]:-}" ] || problems+=("$name: not reported by ECS")
done

if [ "${#problems[@]}" -gt 0 ]; then
  for problem in "${problems[@]}"; do
    echo "::error::$problem" >&2
    echo "$problem" >&2
  done
  print_events
  exit 1
fi
echo "All services are stable and run the applied task definitions."
