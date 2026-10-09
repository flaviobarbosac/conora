#!/usr/bin/env bash
# Restores the conora.com.br Route53 zone and the A records for apex + api (-> EC2 Elastic IP).
# Intended for GitHub Actions with OIDC credentials. Never changes the registrar.
set -euo pipefail

DOMAIN="${DOMAIN:-conora.com.br}"
REGION="${AWS_REGION:-sa-east-1}"
INSTANCE_NAME="${INSTANCE_NAME:-conora-dev-app}"
EXPECTED_ACCOUNT="371664303999"

echo "==> Account / region"
ACCOUNT=$(aws sts get-caller-identity --query Account --output text)
echo "ACCOUNT=$ACCOUNT REGION=$REGION DOMAIN=$DOMAIN"
if [[ "$ACCOUNT" != "$EXPECTED_ACCOUNT" ]]; then
  echo "ERROR: wrong AWS account (expected $EXPECTED_ACCOUNT)" >&2
  exit 1
fi

echo "==> EIP for $INSTANCE_NAME"
INSTANCE_ID=$(aws ec2 describe-instances --region "$REGION" \
  --filters "Name=tag:Name,Values=$INSTANCE_NAME" "Name=instance-state-name,Values=running" \
  --query 'Reservations[0].Instances[0].InstanceId' --output text)
if [[ -z "$INSTANCE_ID" || "$INSTANCE_ID" == "None" ]]; then
  echo "ERROR: running instance $INSTANCE_NAME not found" >&2
  exit 1
fi
EIP=$(aws ec2 describe-addresses --region "$REGION" \
  --filters "Name=instance-id,Values=$INSTANCE_ID" \
  --query 'Addresses[0].PublicIp' --output text)
if [[ -z "$EIP" || "$EIP" == "None" ]]; then
  EIP=$(aws ec2 describe-instances --region "$REGION" --instance-ids "$INSTANCE_ID" \
    --query 'Reservations[0].Instances[0].PublicIpAddress' --output text)
fi
if [[ -z "$EIP" || "$EIP" == "None" ]]; then
  echo "ERROR: could not resolve public IP for $INSTANCE_ID" >&2
  exit 1
fi
echo "INSTANCE_ID=$INSTANCE_ID EIP=$EIP"

echo "==> Hosted zone"
ZONE_ID=$(aws route53 list-hosted-zones-by-name --dns-name "$DOMAIN" --max-items 1 \
  --query "HostedZones[?Name=='${DOMAIN}.'].Id | [0]" --output text | sed 's#/hostedzone/##')
if [[ -z "$ZONE_ID" || "$ZONE_ID" == "None" ]]; then
  ZONE_ID=$(aws route53 create-hosted-zone --name "$DOMAIN" \
    --caller-reference "conora-dns-fix-$(date +%s)" \
    --query 'HostedZone.Id' --output text | sed 's#/hostedzone/##')
  echo "Created ZONE_ID=$ZONE_ID"
else
  echo "Using existing ZONE_ID=$ZONE_ID"
fi

NS=$(aws route53 get-hosted-zone --id "$ZONE_ID" --query 'DelegationSet.NameServers' --output json)

upsert_a() {
  local fqdn="$1"
  local batch
  batch=$(cat <<EOF
{
  "Comment": "fix-route53-dns upsert A ${fqdn}",
  "Changes": [{
    "Action": "UPSERT",
    "ResourceRecordSet": {
      "Name": "${fqdn}",
      "Type": "A",
      "TTL": 300,
      "ResourceRecords": [{"Value": "${EIP}"}]
    }
  }]
}
EOF
)
  echo "UPSERT A ${fqdn} -> ${EIP}"
  aws route53 change-resource-record-sets --hosted-zone-id "$ZONE_ID" --change-batch "$batch" >/dev/null
}

upsert_a "$DOMAIN"
upsert_a "api.${DOMAIN}"

echo "==> Zone records (A/NS)"
aws route53 list-resource-record-sets --hosted-zone-id "$ZONE_ID" \
  --query "ResourceRecordSets[?Type=='A' || Type=='NS'].[Name,Type,TTL,ResourceRecords[0].Value]" \
  --output table

echo ""
echo "======== Registro.br must delegate ${DOMAIN} to these NS ========"
echo "$NS"
echo "ZONE_ID=$ZONE_ID EIP=$EIP"
echo "See infra/scripts/REGISTRO-BR-NS.md"
echo "=================================================================="

mkdir -p /tmp/dns-out
echo "$ZONE_ID" > /tmp/dns-out/zone_id.txt
echo "$EIP" > /tmp/dns-out/eip.txt
echo "$NS" > /tmp/dns-out/ns.json
