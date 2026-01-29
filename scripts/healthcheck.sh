#!/bin/bash
# Whispa Self-Hosting Health Check Script
# Usage: ./healthcheck.sh <domain>
# Example: ./healthcheck.sh whispa.company.com

set -e

DOMAIN="${1:-}"
API_DOMAIN="${2:-api.$DOMAIN}"

if [ -z "$DOMAIN" ]; then
    echo "Usage: $0 <domain> [api-domain]"
    echo "Example: $0 whispa.company.com"
    exit 1
fi

echo "=== Whispa Health Check ==="
echo "Frontend: https://$DOMAIN"
echo "Backend:  https://$API_DOMAIN"
echo ""

# Check frontend
echo -n "Frontend health: "
FRONTEND_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "https://$DOMAIN" || echo "failed")
if [ "$FRONTEND_STATUS" = "200" ]; then
    echo "OK ($FRONTEND_STATUS)"
else
    echo "FAILED ($FRONTEND_STATUS)"
fi

# Check backend health endpoint
echo -n "Backend health:  "
BACKEND_RESPONSE=$(curl -s "https://$API_DOMAIN/health" 2>/dev/null || echo '{"status":"failed"}')
BACKEND_STATUS=$(echo "$BACKEND_RESPONSE" | grep -o '"status":"[^"]*"' | cut -d'"' -f4 || echo "unknown")
if [ "$BACKEND_STATUS" = "healthy" ]; then
    echo "OK ($BACKEND_STATUS)"
else
    echo "FAILED ($BACKEND_STATUS)"
    echo "Response: $BACKEND_RESPONSE"
fi

# Check SSL certificate
echo -n "SSL certificate: "
SSL_EXPIRY=$(echo | openssl s_client -servername "$DOMAIN" -connect "$DOMAIN:443" 2>/dev/null | openssl x509 -noout -enddate 2>/dev/null | cut -d= -f2 || echo "failed")
if [ "$SSL_EXPIRY" != "failed" ]; then
    echo "OK (expires: $SSL_EXPIRY)"
else
    echo "FAILED (could not verify)"
fi

# Check API docs
echo -n "API docs:        "
DOCS_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "https://$API_DOMAIN/docs" || echo "failed")
if [ "$DOCS_STATUS" = "200" ]; then
    echo "OK ($DOCS_STATUS)"
else
    echo "FAILED ($DOCS_STATUS)"
fi

echo ""
echo "=== Check Complete ==="
