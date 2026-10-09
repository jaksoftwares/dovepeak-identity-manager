#!/bin/bash
# Creates the least-privilege service account the Management API uses to administer Keycloak.
#
# The account receives only the master-realm "create-realm" role. Keycloak automatically grants
# the creator of a realm administrative rights over that realm alone, so the Management API can
# manage the realms it provisions but cannot read or change any other realm (threat model E-01).
#
# Idempotent: safe to run on every stack start.
set -euo pipefail

: "${KEYCLOAK_URL:?}"
: "${KEYCLOAK_ADMIN_USERNAME:?}"
: "${KEYCLOAK_ADMIN_PASSWORD:?}"
: "${MANAGEMENT_CLIENT_ID:?}"
: "${MANAGEMENT_CLIENT_SECRET:?}"
  
KCADM=/opt/keycloak/bin/kcadm.sh
CONFIG=/tmp/kcadm.config
kc() { "$KCADM" "$@" --config "$CONFIG"; }

echo "Authenticating to Keycloak master realm..."
kc config credentials --server "$KEYCLOAK_URL" --realm master \
  --user "$KEYCLOAK_ADMIN_USERNAME" --password "$KEYCLOAK_ADMIN_PASSWORD" >/dev/null

client_uuid=$(kc get clients -r master -q "clientId=$MANAGEMENT_CLIENT_ID" --fields id --format csv --noquotes)

if [ -z "$client_uuid" ]; then
  echo "Creating client '$MANAGEMENT_CLIENT_ID'..."
  kc create clients -r master \
    -s "clientId=$MANAGEMENT_CLIENT_ID" \
    -s "name=Dovepeak Management API" \
    -s "description=Service account used by the Dovepeak Management API to provision tenant realms" \
    -s enabled=true \
    -s publicClient=false \
    -s clientAuthenticatorType=client-secret \
    -s "secret=$MANAGEMENT_CLIENT_SECRET" \
    -s serviceAccountsEnabled=true \
    -s standardFlowEnabled=false \
    -s implicitFlowEnabled=false \
    -s directAccessGrantsEnabled=false \
    -s frontchannelLogout=false >/dev/null
else
  echo "Client '$MANAGEMENT_CLIENT_ID' exists; ensuring secret is current..."
  kc update "clients/$client_uuid" -r master -s "secret=$MANAGEMENT_CLIENT_SECRET" >/dev/null
fi

echo "Granting 'create-realm' to the service account..."
kc add-roles -r master --uusername "service-account-$MANAGEMENT_CLIENT_ID" --rolename create-realm

# Keycloak grants ~18 admin roles per realm the account creates. If those roles were written into the access
# token, its size would grow by ~475 bytes per tenant and exceed HTTP header limits after a few hundred realms
# (measured in milestone M1.6). Admin permissions are evaluated from stored role mappings, not token claims,
# so the claim-producing scopes are removed and the token stays a constant ~1 KB.
client_uuid=$(kc get clients -r master -q "clientId=$MANAGEMENT_CLIENT_ID" --fields id --format csv --noquotes)
assigned=$(kc get "clients/$client_uuid/default-client-scopes" -r master --fields id,name --format csv --noquotes)
# The Keycloak image has no awk or sed; parse "id,name" lines with bash only.
while IFS=, read -r scope_id scope_name; do
  case "$scope_name" in
    roles|profile|email|web-origins)
      echo "Removing '$scope_name' scope from the service account token..."
      kc delete "clients/$client_uuid/default-client-scopes/$scope_id" -r master
      ;;
  esac
done <<< "$assigned"

echo "Bootstrap complete."
