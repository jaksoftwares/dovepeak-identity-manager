#!/usr/bin/env bash
# Runs a k6 load test against the production compose deployment (milestone M6.4).
#
#   tests/load/run.sh [signin.js] [k6 options...]       e.g.  VUS=20 DURATION=5m tests/load/run.sh
#
# All load comes from one address, so the per-IP edge limits are lifted for the duration of the run and restored
# afterwards (they are tested separately; here the question is how much the identity engine can serve).
# Never run against production during business hours: it creates a realm and real sign-in load.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
script="${1:-signin.js}"; shift || true
cd "$root/deploy/production"
export MSYS_NO_PATHCONV=1

env_value() { sed -n "s/^$1=//p" .env | tail -1; }
host_path() { if command -v cygpath >/dev/null; then cygpath -w "$1"; else echo "$1"; fi; }
identity_host=$(env_value IDENTITY_HOST)

state_dir=$(mktemp -d)
restore_limits() {
  echo "Restoring edge rate limits"
  docker compose up -d --no-deps --wait edge >/dev/null 2>&1
  rm -rf "$state_dir"
}
trap restore_limits EXIT

docker compose cp caddy:/data/caddy/pki/authorities/local/root.crt "$(host_path "$state_dir/caddy-root.crt")" 2>/dev/null || true
ca_env=()
[ -s "$state_dir/caddy-root.crt" ] && ca_env=(-e SSL_CERT_FILE=/state/caddy-root.crt)

echo "Lifting edge rate limits for the test"
EDGE_RATELIMIT_EMAIL_PER_MINUTE=100000 EDGE_RATELIMIT_CREDENTIALS_PER_MINUTE=100000 EDGE_RATELIMIT_GENERAL_PER_MINUTE=100000 \
EDGE_RATELIMIT_EMAIL_BURST=100000 EDGE_RATELIMIT_CREDENTIALS_BURST=100000 EDGE_RATELIMIT_GENERAL_BURST=100000 \
  docker compose up -d --no-deps --wait edge >/dev/null 2>&1

name="k6-$$"
docker create --name "$name" --network dovepeak_frontend --add-host "$identity_host:172.30.0.5" \
  -v "$(host_path "$root/tests/load"):/scripts:ro" -v "$(host_path "$state_dir"):/state" "${ca_env[@]}" \
  -e IDENTITY_URL="https://$identity_host" -e ADMIN_URL=http://keycloak-1:8080 \
  -e ADMIN_USER="$(env_value KEYCLOAK_ADMIN_USERNAME)" -e ADMIN_PASSWORD="$(env_value KEYCLOAK_ADMIN_PASSWORD)" \
  -e VUS="${VUS:-10}" -e DURATION="${DURATION:-2m}" \
  grafana/k6:2.3.0 run "$@" "/scripts/$script" >/dev/null
docker network connect dovepeak_default "$name"
status=0
docker start -a "$name" || status=$?
docker rm "$name" >/dev/null
exit $status
