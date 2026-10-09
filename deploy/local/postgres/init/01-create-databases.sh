#!/bin/sh
# Creates separate databases and least-privilege roles for the Management API and Keycloak.
# Runs once, when the PostgreSQL data volume is first initialised.
set -e

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
  -v dovepeak_password="$DOVEPEAK_DB_PASSWORD" \
  -v keycloak_password="$KEYCLOAK_DB_PASSWORD" <<-'EOSQL'
	CREATE ROLE dovepeak LOGIN PASSWORD :'dovepeak_password';
	CREATE DATABASE dovepeak OWNER dovepeak;
	REVOKE ALL ON DATABASE dovepeak FROM PUBLIC;

	CREATE ROLE keycloak LOGIN PASSWORD :'keycloak_password';
	CREATE DATABASE keycloak OWNER keycloak;
	REVOKE ALL ON DATABASE keycloak FROM PUBLIC;
EOSQL
