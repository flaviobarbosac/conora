#!/usr/bin/env bash
# Generates .env (compose interpolation) and .env.app (ASP.NET settings) on the EC2 host
# from Secrets Manager. Never commit the generated files.
#
# Usage: generate-env.sh <secret-prefix> <output-dir> <api-fqdn> <domain> [aspnet-env]
#   e.g. generate-env.sh conora/dev /opt/conora api.conora.com.br conora.com.br
set -euo pipefail

SECRET_PREFIX="${1:?secret prefix, e.g. conora/dev}"
OUT_DIR="${2:?output dir, e.g. /opt/conora}"
API_FQDN="${3:?api fqdn, e.g. api.conora.com.br}"
DOMAIN="${4:?domain, e.g. conora.com.br}"
ASPNET_ENV="${5:-Production}"
REGION="${AWS_REGION:-sa-east-1}"

APP_CORE=$(aws secretsmanager get-secret-value --secret-id "${SECRET_PREFIX}/app-core" --query SecretString --output text --region "$REGION")
INTEGRATIONS=$(aws secretsmanager get-secret-value --secret-id "${SECRET_PREFIX}/integrations" --query SecretString --output text --region "$REGION")

ACCOUNT=$(aws sts get-caller-identity --query Account --output text --region "$REGION")
ENV_NAME="${SECRET_PREFIX##*/}"
ECR_HOST="${ACCOUNT}.dkr.ecr.${REGION}.amazonaws.com"

get_app() { echo "$APP_CORE" | jq -r "$1 // empty"; }
get_int() { echo "$INTEGRATIONS" | jq -r "$1 // empty"; }

# docker compose interpolates '$' in env files; escape it.
esc() { printf '%s' "$1" | sed 's/\$/$$/g'; }

DB_CONNECTION=$(esc "$(get_app .connectionString)")
JWT_KEY=$(esc "$(get_app .jwtKey)")
RABBITMQ_USER=$(get_int .rabbitmq.user)
RABBITMQ_PASS=$(esc "$(get_int .rabbitmq.password)")
REDIS_PASSWORD=$(esc "$(get_int .redis.password)")

if [ -z "$DB_CONNECTION" ] || [ -z "$JWT_KEY" ] || [ -z "$RABBITMQ_PASS" ] || [ -z "$REDIS_PASSWORD" ]; then
  echo "ERROR: ${SECRET_PREFIX} secrets are missing required keys (connectionString, jwtKey, rabbitmq, redis)." >&2
  exit 1
fi

umask 077

cat > "${OUT_DIR}/.env" <<EOF
ECR_API=${ECR_HOST}/conora-${ENV_NAME}-api
ECR_WORKER=${ECR_HOST}/conora-${ENV_NAME}-worker
RABBITMQ_USER=${RABBITMQ_USER}
RABBITMQ_PASS=${RABBITMQ_PASS}
REDIS_PASSWORD=${REDIS_PASSWORD}
EOF

cat > "${OUT_DIR}/.env.app" <<EOF
ASPNETCORE_ENVIRONMENT=${ASPNET_ENV}
ASPNETCORE_URLS=http://+:8080
ConnectionStrings__Postgres=${DB_CONNECTION}
ConnectionStrings__Redis=redis:6379,password=${REDIS_PASSWORD},abortConnect=false
ConnectionStrings__RabbitMq=amqp://${RABBITMQ_USER}:${RABBITMQ_PASS}@rabbitmq:5672
Jwt__Key=${JWT_KEY}
Google__ClientId=$(get_int .google.clientId)
Gemini__ApiKey=$(esc "$(get_int .gemini.apiKey)")
Gemini__Model=$(get_int .gemini.model)
Email__From=$(get_int .email.from)
Email__SmtpHost=$(get_int .email.smtpHost)
Email__SmtpPort=$(get_int .email.smtpPort)
Email__User=$(get_int .email.user)
Email__Password=$(esc "$(get_int .email.password)")
Email__UseSsl=$(get_int .email.useSsl)
WhatsApp__VerifyToken=$(esc "$(get_int .whatsapp.verifyToken)")
WhatsApp__AppSecret=$(esc "$(get_int .whatsapp.appSecret)")
Cors__Origins__0=https://${DOMAIN}
Cors__Origins__1=http://${DOMAIN}
Cors__Origins__2=http://localhost:5173
OpenTelemetry__OtlpEndpoint=
EOF

# Allow provisional access via the instance public IP (same-origin front+API on :80).
if PUBLIC_IP=$(curl -fsS --connect-timeout 2 -H "X-aws-ec2-metadata-token: $(curl -fsS -X PUT --connect-timeout 2 -H 'X-aws-ec2-metadata-token-ttl-seconds: 60' http://169.254.169.254/latest/api/token)" http://169.254.169.254/latest/meta-data/public-ipv4 2>/dev/null); then
  if [ -n "$PUBLIC_IP" ]; then
    printf 'Cors__Origins__3=http://%s\n' "$PUBLIC_IP" >> "${OUT_DIR}/.env.app"
  fi
fi


chmod 600 "${OUT_DIR}/.env" "${OUT_DIR}/.env.app"
echo "Generated ${OUT_DIR}/.env and ${OUT_DIR}/.env.app (api=${API_FQDN})"
