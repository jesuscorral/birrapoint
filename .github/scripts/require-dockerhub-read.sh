#!/usr/bin/env bash
# Fails when the Docker Hub read-only pull credentials are missing, empty or blank (T131).
# Terraform requires both values; failing early keeps the run from reaching the approval gate.
#
#   DOCKERHUB_USERNAME=... DOCKERHUB_READ_TOKEN=... require-dockerhub-read.sh
#
# Values come through the environment only. Output names the missing secret, never a value.
set -u

status=0
for name in DOCKERHUB_USERNAME DOCKERHUB_READ_TOKEN; do
  value="${!name:-}"
  if [ -z "${value//[[:space:]]/}" ]; then
    echo "::error::The $name repository secret is missing or empty; the deployment needs Docker Hub credentials to pull images (see infra/ci-setup.md)."
    status=1
  fi
done
exit "$status"
