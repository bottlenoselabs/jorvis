#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

echo "Stopping Jorvis..."
docker compose down "$@"
echo "Stopped."
