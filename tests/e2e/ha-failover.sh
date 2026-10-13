#!/usr/bin/env bash
# Keycloak failover test against the production compose deployment (milestone M6.2).
# Signs a user in, stops Keycloak node 1, proves the session and tokens keep working on node 2, then restarts node 1.
#
#   tests/e2e/ha-failover.sh        # with deploy/production running (its .env supplies hostnames and credentials)
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$root/deploy/production"
export MSYS_NO_PATHCONV=1
# Read single values: .env is Compose syntax, not necessarily valid shell.
env_value() { sed -n "s/^$1=//p" .env | tail -1; }
export IDENTITY_HOST=$(env_value IDENTITY_HOST)
export KEYCLOAK_ADMIN_USERNAME=$(env_value KEYCLOAK_ADMIN_USERNAME)
export KEYCLOAK_ADMIN_PASSWORD=$(env_value KEYCLOAK_ADMIN_PASSWORD)

project=dovepeak
state_dir=$(mktemp -d)
trap 'rm -rf "$state_dir"' EXIT

# Docker Desktop on Windows needs Windows paths for bind mounts; elsewhere this is a no-op.
host_path() { if command -v cygpath >/dev/null; then cygpath -w "$1"; else echo "$1"; fi; }

# Trust Caddy's local CA when the rehearsal uses local_certs; public certificates need nothing.
docker compose cp caddy:/data/caddy/pki/authorities/local/root.crt "$(host_path "$state_dir/caddy-root.crt")" 2>/dev/null \
  || touch "$state_dir/caddy-root.crt"

run_phase() {
  local name="ha-failover-$1-$$"
  docker create --name "$name" --network "${project}_frontend" --add-host "$IDENTITY_HOST:172.30.0.5" \
    -v "$(host_path "$root/tests/e2e"):/e2e:ro" -v "$(host_path "$state_dir"):/state" \
    -e IDENTITY_URL="https://$IDENTITY_HOST" -e ADMIN_URL_1=http://keycloak-1:8080 -e ADMIN_URL_2=http://keycloak-2:8080 \
    -e KEYCLOAK_ADMIN_USERNAME -e KEYCLOAK_ADMIN_PASSWORD -e STATE_FILE=/state/state.json \
    -e NODE_EXTRA_CA_CERTS=/state/caddy-root.crt \
    node:22.23.3-alpine node /e2e/ha-failover.mjs "$1" >/dev/null
  # The admin API is only on the internal network.
  docker network connect "${project}_default" "$name"
  local status=0
  docker start -a "$name" || status=$?
  docker rm "$name" >/dev/null
  return $status
}

echo "== Cluster membership"
docker compose logs keycloak-2 2>/dev/null | grep -o "ISPN000094: Received new cluster view.*" | tail -1 || true

echo "== Phase 1: sign in (both nodes running)"
run_phase setup

echo "== Stopping keycloak-1"
docker compose stop keycloak-1 >/dev/null 2>&1

echo "== Phase 2: verify on keycloak-2 alone"
verify_status=0
run_phase verify || verify_status=$?

echo "== Restarting keycloak-1"
docker compose start keycloak-1 >/dev/null 2>&1
docker compose up -d --wait keycloak-1 >/dev/null 2>&1

run_phase cleanup
exit $verify_status
