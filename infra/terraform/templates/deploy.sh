#!/bin/bash
# Idempotent host deploy: pulls config from S3, renders env files from Secrets Manager,
# logs in to ECR and (re)starts the stack. Runs at first boot and on every SSM deploy.
set -euo pipefail
exec > >(tee -a /var/log/conora-deploy.log) 2>&1

echo "=== Conora deploy ==="
REGION="${aws_region}"
BUCKET="${config_bucket}"
ACCOUNT="${account_id}"
SECRET_PREFIX="${project}/${environment}"
APP_DIR=/opt/conora

dnf install -y docker amazon-ssm-agent jq
systemctl enable --now docker amazon-ssm-agent

if [ ! -x /usr/local/lib/docker/cli-plugins/docker-compose ]; then
  mkdir -p /usr/local/lib/docker/cli-plugins
  curl -fSL "https://github.com/docker/compose/releases/download/v2.32.4/docker-compose-linux-aarch64" \
    -o /usr/local/lib/docker/cli-plugins/docker-compose
  chmod +x /usr/local/lib/docker/cli-plugins/docker-compose
fi

mkdir -p "$${APP_DIR}/app" "$${APP_DIR}/public"
aws s3 cp "s3://$${BUCKET}/docker-compose.yml" "$${APP_DIR}/docker-compose.yml" --region "$${REGION}"
aws s3 cp "s3://$${BUCKET}/Caddyfile" "$${APP_DIR}/Caddyfile" --region "$${REGION}"
aws s3 cp "s3://$${BUCKET}/generate-env.sh" "$${APP_DIR}/generate-env.sh" --region "$${REGION}"
sed -i 's/\r$//' "$${APP_DIR}/generate-env.sh"
chmod +x "$${APP_DIR}/generate-env.sh"

# Front-end static files (uploaded by the front pipeline to s3://BUCKET/front/)
aws s3 sync "s3://$${BUCKET}/front/" "$${APP_DIR}/app/" --delete --region "$${REGION}" || true
# Ensure index.html is never stale relative to hashed assets.
aws s3 cp "s3://$${BUCKET}/front/index.html" "$${APP_DIR}/app/index.html" --region "$${REGION}" || true

# Public marketing / legal pages (landing, privacy, terms)
aws s3 sync "s3://$${BUCKET}/public/" "$${APP_DIR}/public/" --delete --region "$${REGION}" || true

cd "$${APP_DIR}"
export AWS_REGION="$${REGION}"
"$${APP_DIR}/generate-env.sh" "$${SECRET_PREFIX}" "$${APP_DIR}" "${api_fqdn}" "${domain}"

aws ecr get-login-password --region "$${REGION}" \
  | docker login --username AWS --password-stdin "$${ACCOUNT}.dkr.ecr.$${REGION}.amazonaws.com"

# Infra services and Caddy first; app images may not exist yet on a fresh account.
docker compose pull caddy || true
# Recreate Caddy so a changed Caddyfile (bind mount) is actually loaded.
docker compose up -d --remove-orphans --force-recreate caddy

if docker compose pull conora-api conora-worker; then
  docker compose up -d --remove-orphans --force-recreate conora-api conora-worker
else
  echo "WARN: conora-api / conora-worker images not in ECR yet (run infra/scripts/push-images.ps1)."
fi

docker compose ps
docker image prune -f >/dev/null 2>&1 || true
echo "=== Deploy finished ==="
