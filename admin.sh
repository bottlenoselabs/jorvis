#!/usr/bin/env bash
# Owner-only administration: ./admin.sh invite [--email <address>] [--hours <n>] | users | set-username <email> <username> | remove-user <email>
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

docker compose exec -e Logging__LogLevel__Default=Warning jorvis-api dotnet Jorvis.Api.dll "${@:-help}"
