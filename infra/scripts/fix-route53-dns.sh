#!/usr/bin/env bash
set -euo pipefail

echo "Conora fix-route53-dns placeholder."
echo "Implement Route53 restore when DNS/hosted zone exist (same pattern as ClampFY)."
mkdir -p /tmp/dns-out
echo "pending" > /tmp/dns-out/status.txt
exit 1
