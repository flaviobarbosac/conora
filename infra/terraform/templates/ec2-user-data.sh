#!/bin/bash
set -euo pipefail
exec > /var/log/conora-user-data.log 2>&1

echo "=== Conora EC2 bootstrap ==="
dnf update -y
dnf install -y docker amazon-ssm-agent jq
systemctl enable --now docker amazon-ssm-agent

# docker compose plugin for aarch64 (t4g / Graviton)
mkdir -p /usr/local/lib/docker/cli-plugins
curl -fSL "https://github.com/docker/compose/releases/download/v2.32.4/docker-compose-linux-aarch64" \
  -o /usr/local/lib/docker/cli-plugins/docker-compose
chmod +x /usr/local/lib/docker/cli-plugins/docker-compose

aws s3 cp "s3://${config_bucket}/deploy.sh" /tmp/conora-deploy.sh --region ${aws_region}
chmod +x /tmp/conora-deploy.sh
/tmp/conora-deploy.sh
