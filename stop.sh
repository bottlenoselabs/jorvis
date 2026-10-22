#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

echo "Stopping Jorvis..."
# "*" includes the optional tunnel, which `docker compose down` would otherwise leave running.
docker compose --profile "*" down "$@"
echo "Stopped."
