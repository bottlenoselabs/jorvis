#!/usr/bin/env bash
# Checks that the public Sign In and Join app keeps a CSP that stops the browser sending anything to another origin
# and running anything but its own scripts.
# Usage: check-auth-csp.sh [base-url]   (default http://localhost:5200, the running docker compose stack)
set -euo pipefail

BASE_URL="${1:-http://localhost:5200}"
failures=0

for path in /auth/sign-in /auth/join /auth/css/app.css /auth/passkeys.js; do
  csp="$(curl -sS -D - -o /dev/null "$BASE_URL$path" | tr -d '\r' | grep -i '^content-security-policy:' || true)"
  directive() { grep -oiE "(^|[: ;])$1 [^;]*" <<<"$csp" | sed -E "s/^[: ;]*$1 //I" || true; }

  problems=()
  [[ -n "$csp" ]] || problems+=("no Content-Security-Policy header")
  [[ "$(directive default-src)" == "'self'" ]] || problems+=("default-src is not 'self'")
  [[ "$(directive connect-src)" == "'self'" ]] || problems+=("connect-src is not 'self'")
  [[ "$(directive form-action)" == "'self'" ]] || problems+=("form-action is not 'self'")
  [[ "$(directive frame-ancestors)" == "'none'" ]] || problems+=("frame-ancestors is not 'none'")
  [[ "$(directive object-src)" == "'none'" ]] || problems+=("object-src is not 'none'")
  scripts="$(directive script-src)"
  [[ "$scripts" == "'self' 'wasm-unsafe-eval' 'sha256-"* ]] || problems+=("script-src is not 'self' plus wasm-unsafe-eval and one hash")
  [[ "$scripts" != *"'unsafe-inline'"* && "$scripts" != *"'unsafe-eval'"* && "$scripts" != *http* ]] || problems+=("script-src allows inline, eval or another origin")
  [[ "$csp" != *'*'* ]] || problems+=("contains a wildcard")

  if ((${#problems[@]})); then
    printf 'FAIL %s: %s\n' "$path" "$(IFS=,; echo "${problems[*]}")"
    failures=$((failures + 1))
  else
    echo "ok   $path"
  fi
done

exit $((failures > 0))
