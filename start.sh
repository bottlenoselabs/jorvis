#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

# Enable the Cloudflare tunnel only when its token file exists (kept outside the repo).
CLOUDFLARE_TUNNEL_TOKEN_FILEPATH="${JORVIS_CLOUDFLARE_TUNNEL_TOKEN_FILEPATH:-$HOME/.config/jorvis/cloudflare-tunnel-token}"
if [[ -f "$CLOUDFLARE_TUNNEL_TOKEN_FILEPATH" ]]; then
  export JORVIS_UID="$(id -u)" JORVIS_GID="$(id -g)"
  export COMPOSE_PROFILES="${COMPOSE_PROFILES:+$COMPOSE_PROFILES,}tunnel"
fi

echo "Starting Jorvis..."
docker compose up --build --detach --wait "$@"

echo
echo "Jorvis is running:"
echo "  Web: http://localhost:5200"
echo "  Api: http://localhost:5117"
