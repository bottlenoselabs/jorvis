#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

# Enable the Cloudflare tunnel only when its token file exists (kept outside the repo).
CLOUDFLARE_TUNNEL_TOKEN_FILEPATH="${JORVIS_CLOUDFLARE_TUNNEL_TOKEN_FILEPATH:-$HOME/.config/jorvis/cloudflare-tunnel-token}"
if [[ -f "$CLOUDFLARE_TUNNEL_TOKEN_FILEPATH" ]]; then
  export JORVIS_UID="$(id -u)" JORVIS_GID="$(id -g)"
  export COMPOSE_PROFILES="${COMPOSE_PROFILES:+$COMPOSE_PROFILES,}tunnel"
fi

# Passkeys are tied to the address Jorvis is opened at, so warn before accounts get created at the wrong one.
if [[ -n "${COMPOSE_PROFILES:-}" && -z "${JORVIS_PUBLIC_URL:-}" ]] && ! grep -qs '^JORVIS_PUBLIC_URL=' .env; then
  echo "Warning: the tunnel is enabled but JORVIS_PUBLIC_URL is not set. Passkeys created now would be tied to"
  echo "         localhost and would not work at your public address. Set it (see README) before creating accounts."
  echo
fi

echo "Starting Jorvis (${JORVIS_ENVIRONMENT:-Production})..."
docker compose up --build --detach --wait "$@"

echo
echo "Jorvis is running:"
echo "  Web: http://localhost:5200"
echo "  Api: http://localhost:5117"
echo
echo "Invite someone (or create your own account): ./admin.sh invite"
