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

umask 077
APP_CORE_FILE="${OUT_DIR}/.app-core.json"
INT_FILE="${OUT_DIR}/.integrations.json"
aws secretsmanager get-secret-value --secret-id "${SECRET_PREFIX}/app-core" --query SecretString --output text --region "$REGION" > "$APP_CORE_FILE"
aws secretsmanager get-secret-value --secret-id "${SECRET_PREFIX}/integrations" --query SecretString --output text --region "$REGION" > "$INT_FILE"
trap 'rm -f "$APP_CORE_FILE" "$INT_FILE"' EXIT

ACCOUNT=$(aws sts get-caller-identity --query Account --output text --region "$REGION")
ENV_NAME="${SECRET_PREFIX##*/}"
ECR_HOST="${ACCOUNT}.dkr.ecr.${REGION}.amazonaws.com"

# Secrets saved from Windows may start with a UTF-8 BOM, which jq rejects.
json_get() {
  local file="$1"
  local expr="$2"
  python3 - "$file" "$expr" <<'PY'
import json, pathlib, sys
raw = pathlib.Path(sys.argv[1]).read_bytes()
# Windows editors and the AWS CLI can prefix the secret with a UTF-8 BOM,
# sometimes stored again as the latin-1 mojibake of that BOM.
for prefix in (b"\xef\xbb\xbf", b"\xc3\xaf\xc2\xbb\xc2\xbf"):
    if raw.startswith(prefix):
        raw = raw[len(prefix):]
text = raw.decode("utf-8-sig").lstrip("\ufeff")
data = json.loads(text)
cur = data
for part in sys.argv[2].strip(".").split("."):
    if part == "":
        continue
    if not isinstance(cur, dict) or part not in cur or cur[part] is None:
        sys.exit(0)
    cur = cur[part]
if isinstance(cur, bool):
    print("true" if cur else "false")
elif isinstance(cur, (dict, list)):
    print(json.dumps(cur))
else:
    print(cur)
PY
}

get_app() { json_get "$APP_CORE_FILE" "$1"; }
get_int() { json_get "$INT_FILE" "$1"; }

# docker compose interpolates '$' in env files; escape it.
esc() { printf '%s' "$1" | sed 's/\$/$$/g'; }

DB_CONNECTION=$(esc "$(get_app .connectionString)")
JWT_KEY=$(esc "$(get_app .jwtKey)")

if [ -z "$DB_CONNECTION" ] || [ -z "$JWT_KEY" ]; then
  echo "ERROR: ${SECRET_PREFIX}/app-core is missing connectionString or jwtKey." >&2
  exit 1
fi

# SES needs the configuration set header for bounce/complaint events; other SMTP relays must not get it.
# Older secrets have no configurationSet field, so fall back to the SES default when the host is SES.
EMAIL_CONFIGURATION_SET=$(get_int .email.configurationSet)
if [ -z "$EMAIL_CONFIGURATION_SET" ] && [[ "$(get_int .email.smtpHost)" == *amazonaws.com ]]; then
  EMAIL_CONFIGURATION_SET="conora-${ENV_NAME}-transactional"
fi

umask 077

cat > "${OUT_DIR}/.env" <<EOF
ECR_API=${ECR_HOST}/conora-${ENV_NAME}-api
ECR_WORKER=${ECR_HOST}/conora-${ENV_NAME}-worker
EOF

cat > "${OUT_DIR}/.env.app" <<EOF
ASPNETCORE_ENVIRONMENT=${ASPNET_ENV}
ASPNETCORE_URLS=http://+:8080
ConnectionStrings__Postgres=${DB_CONNECTION}
Jwt__Key=${JWT_KEY}
Jwt__Issuer=conora-api
Jwt__Audience=conora-app
Google__ClientId=$(get_int .google.clientId)
Gemini__ApiKey=$(esc "$(get_int .gemini.apiKey)")
Gemini__Model=$(get_int .gemini.model)
Email__From=$(get_int .email.from)
Email__SmtpHost=$(get_int .email.smtpHost)
Email__SmtpPort=$(get_int .email.smtpPort)
Email__User=$(get_int .email.user)
Email__Password=$(esc "$(get_int .email.password)")
Email__UseSsl=$(get_int .email.useSsl)
Email__ConfigurationSet=${EMAIL_CONFIGURATION_SET}
WhatsApp__VerifyToken=$(esc "$(get_int .whatsapp.verifyToken)")
WhatsApp__AppSecret=$(esc "$(get_int .whatsapp.appSecret)")
WhatsApp__AccessToken=$(esc "$(get_int .whatsapp.accessToken)")
WhatsApp__PhoneNumberId=$(get_int .whatsapp.phoneNumberId)
Cors__Origins__0=https://${DOMAIN}
Cors__Origins__1=http://${DOMAIN}
Cors__Origins__2=https://www.${DOMAIN}
Cors__Origins__3=http://www.${DOMAIN}
OpenTelemetry__OtlpEndpoint=
EOF

APPLE_CLIENT_ID=$(get_int .apple.clientId)
APPLE_SERVICES_ID=$(get_int .apple.servicesId)
if [ -n "$APPLE_CLIENT_ID" ]; then
  printf 'Apple__ClientId=%s\n' "$APPLE_CLIENT_ID" >> "${OUT_DIR}/.env.app"
fi
if [ -n "$APPLE_SERVICES_ID" ]; then
  printf 'Apple__ServicesId=%s\n' "$APPLE_SERVICES_ID" >> "${OUT_DIR}/.env.app"
fi

APPLE_KEY_ID=$(get_int .apple.keyId)
APPLE_PRIVATE_KEY=$(get_int .apple.privateKey | tr -d '\r\n')
if [ -n "$APPLE_KEY_ID" ] && [ -n "$APPLE_PRIVATE_KEY" ]; then
  printf 'Apple__TeamId=%s\n' "$(get_int .apple.teamId)" >> "${OUT_DIR}/.env.app"
  printf 'Apple__KeyId=%s\n' "$APPLE_KEY_ID" >> "${OUT_DIR}/.env.app"
  printf 'Apple__PrivateKey=%s\n' "$APPLE_PRIVATE_KEY" >> "${OUT_DIR}/.env.app"
fi

# Store billing: key files are written to ./secrets, mounted read-only at /run/secrets in conora-api.
# Without these values purchase confirmation fails closed, so a missing block only disables billing.
SECRETS_DIR="${OUT_DIR}/secrets"
mkdir -p "$SECRETS_DIR"
# The API container runs as a non-root user, so the mounted files must be world-readable on this single-purpose host.
chmod 755 "$SECRETS_DIR"
STORE_NOTIFICATION_KEY=$(get_int .storeBilling.notificationKey)
if [ -n "$STORE_NOTIFICATION_KEY" ]; then
  printf 'StoreBilling__NotificationKey=%s\n' "$(esc "$STORE_NOTIFICATION_KEY")" >> "${OUT_DIR}/.env.app"
fi
STORE_APPLE_KEY=$(get_int .storeBilling.applePrivateKey)
if [ -n "$STORE_APPLE_KEY" ]; then
  printf '%s\n' "$STORE_APPLE_KEY" > "${SECRETS_DIR}/SubscriptionKey.p8"
  printf 'StoreBilling__AppleIssuerId=%s\n' "$(get_int .storeBilling.appleIssuerId)" >> "${OUT_DIR}/.env.app"
  printf 'StoreBilling__AppleKeyId=%s\n' "$(get_int .storeBilling.appleKeyId)" >> "${OUT_DIR}/.env.app"
  printf 'StoreBilling__ApplePrivateKeyPath=/run/secrets/SubscriptionKey.p8\n' >> "${OUT_DIR}/.env.app"
fi
STORE_GOOGLE_JSON=$(get_int .storeBilling.googleServiceAccount)
if [ -n "$STORE_GOOGLE_JSON" ]; then
  printf '%s\n' "$STORE_GOOGLE_JSON" > "${SECRETS_DIR}/play-service-account.json"
  printf 'StoreBilling__GoogleServiceAccountJsonPath=/run/secrets/play-service-account.json\n' >> "${OUT_DIR}/.env.app"
fi
chmod 644 "$SECRETS_DIR"/* 2>/dev/null || true

# Allow provisional access via the instance public IP (same-origin front+API on :80).
if PUBLIC_IP=$(curl -fsS --connect-timeout 2 -H "X-aws-ec2-metadata-token: $(curl -fsS -X PUT --connect-timeout 2 -H 'X-aws-ec2-metadata-token-ttl-seconds: 60' http://169.254.169.254/latest/api/token)" http://169.254.169.254/latest/meta-data/public-ipv4 2>/dev/null); then
  if [ -n "$PUBLIC_IP" ]; then
    printf 'Cors__Origins__4=http://%s\n' "$PUBLIC_IP" >> "${OUT_DIR}/.env.app"
  fi
fi


chmod 600 "${OUT_DIR}/.env" "${OUT_DIR}/.env.app"
echo "Generated ${OUT_DIR}/.env and ${OUT_DIR}/.env.app (api=${API_FQDN})"
