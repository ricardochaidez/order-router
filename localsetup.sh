#!/usr/bin/env bash
# Brings the local docker compose stack down and back up (Linux and macOS).
set -euo pipefail

# docker-compose.yml sits next to this script, so run from here whatever the caller's directory.
cd "$(dirname "$0")"

docker compose down
docker compose up -d --wait
