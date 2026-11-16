#!/usr/bin/env bash
# Host-side deploy of the shared Segundo Cérebro (runs on the Conora EC2 via SSM).
#   1. Pulls compose, migrations and bootstrap script from the config bucket.
#   2. Renders .env.brain from Secrets Manager (never leaves the host).
#   3. Bootstraps the isolated "brain" database and applies pending migrations.
#   4. Pulls the image from ECR and (re)starts the brain-mcp container.
#   5. With --caddy: installs the bucket Caddyfile in place and hot-reloads Caddy
#      (same inode, zero downtime for the Conora site/API).
#
# Usage: deploy-brain.sh <config-bucket> [--caddy]
set -euo pipefail
exec > >(tee -a /var/log/conora-brain-deploy.log) 2>&1

BUCKET="${1:?config bucket}"
WITH_CADDY="${2:-}"
REGION="${AWS_REGION:-sa-east-1}"
export AWS_REGION="$REGION"
SECRET_PREFIX="conora/dev"
DIR=/opt/conora-brain
APP_DIR=/opt/conora

echo "=== brain deploy $(date -Is) ==="
mkdir -p "$DIR/database"
aws s3 sync "s3://${BUCKET}/brain/database/" "$DIR/database/" --delete --region "$REGION"
aws s3 cp "s3://${BUCKET}/brain/docker-compose.yml" "$DIR/docker-compose.yml" --region "$REGION"
aws s3 cp "s3://${BUCKET}/brain/bootstrap-db.sh" "$DIR/bootstrap-db.sh" --region "$REGION"
sed -i 's/\r$//' "$DIR/bootstrap-db.sh"
chmod 700 "$DIR/bootstrap-db.sh"

umask 077
ACCOUNT=$(aws sts get-caller-identity --query Account --output text)
APP_CORE=$(aws secretsmanager get-secret-value --secret-id "${SECRET_PREFIX}/app-core" --query SecretString --output text)
BRAIN=$(aws secretsmanager get-secret-value --secret-id "${SECRET_PREFIX}/brain" --query SecretString --output text)

# Parse both secrets into shell-safe assignments (values are never echoed).
eval "$(APP_CORE="$APP_CORE" BRAIN="$BRAIN" python3 - <<'PY'
import json, os, shlex
def load(raw):
    raw = raw.lstrip("﻿")
    return json.loads(raw)
core = load(os.environ["APP_CORE"])
brain = load(os.environ["BRAIN"])
parts = {}
for item in core["connectionString"].split(";"):
    if "=" in item:
        k, v = item.split("=", 1)
        parts[k.strip().lower()] = v.strip()
out = {
    "MASTER_HOST": parts["host"],
    "MASTER_PORT": parts.get("port", "5432"),
    "APP_DB": parts["database"],
    "MASTER_USER": parts["username"],
    "MASTER_PASSWORD": parts["password"],
    "BRAIN_HOST": brain["host"],
    "BRAIN_PORT": str(brain["port"]),
    "BRAIN_DB": brain["database"],
    "AGENT_USER": brain["agentUser"],
    "AGENT_PASSWORD": brain["agentPassword"],
    "WRITER_USER": brain["writerUser"],
    "WRITER_PASSWORD": brain["writerPassword"],
}
for k, v in out.items():
    print(f"{k}={shlex.quote(v)}")
PY
)"

cat > "$DIR/.env.brain" <<EOF
BRAIN_DB_HOST=${BRAIN_HOST}
BRAIN_DB_PORT=${BRAIN_PORT}
BRAIN_DB_NAME=${BRAIN_DB}
BRAIN_DB_SSL=require
BRAIN_DB_AGENT_USER=${AGENT_USER}
BRAIN_DB_AGENT_PASSWORD=${AGENT_PASSWORD}
BRAIN_DB_WRITER_USER=${WRITER_USER}
BRAIN_DB_WRITER_PASSWORD=${WRITER_PASSWORD}
PORT=8080
EOF
printf 'BRAIN_IMAGE=%s.dkr.ecr.%s.amazonaws.com/conora-dev-brain:latest\n' "$ACCOUNT" "$REGION" > "$DIR/.env"
chmod 600 "$DIR/.env.brain" "$DIR/.env"

echo "--- database bootstrap"
PGHOST="$MASTER_HOST" PGPORT="$MASTER_PORT" MASTER_USER="$MASTER_USER" MASTER_PASSWORD="$MASTER_PASSWORD" \
APP_DB="$APP_DB" AGENT_PASSWORD="$AGENT_PASSWORD" WRITER_PASSWORD="$WRITER_PASSWORD" \
BRAIN_DB="$BRAIN_DB" SQL_DIR="$DIR/database" \
  "$DIR/bootstrap-db.sh"

echo "--- container"
aws ecr get-login-password --region "$REGION" \
  | docker login --username AWS --password-stdin "${ACCOUNT}.dkr.ecr.${REGION}.amazonaws.com" >/dev/null
cd "$DIR"
docker compose pull brain-mcp
docker compose up -d --force-recreate brain-mcp

for i in $(seq 1 20); do
  if docker exec brain-mcp wget -qO- http://127.0.0.1:8080/health >/dev/null 2>&1; then
    echo "brain-mcp healthy"
    break
  fi
  if [ "$i" = "20" ]; then
    docker logs --tail 50 brain-mcp
    echo "ERROR: brain-mcp did not become healthy" >&2
    exit 1
  fi
  sleep 2
done

if [ "$WITH_CADDY" = "--caddy" ]; then
  echo "--- caddy"
  TMP=$(mktemp)
  aws s3 cp "s3://${BUCKET}/Caddyfile" "$TMP" --region "$REGION"
  docker run --rm -v "$TMP:/etc/caddy/Caddyfile:ro" caddy:2-alpine \
    caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile
  # Keep the inode: the Caddyfile is a single-file bind mount in conora-caddy.
  cat "$TMP" > "$APP_DIR/Caddyfile"
  rm -f "$TMP"
  docker exec conora-caddy caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile
  echo "caddy reloaded"
fi

docker compose ps
echo "=== brain deploy finished ==="
