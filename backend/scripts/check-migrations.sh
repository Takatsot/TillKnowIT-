#!/usr/bin/env bash
# Fails if any module's EF model has changes that are not in a migration.
set -euo pipefail
cd "$(dirname "$0")/.."

status=0
for module in ShopCore Catalogue Marketplace Messaging Identity Payments Wallet Platform Customers Bookings Billing Finance; do
  if dotnet ef migrations has-pending-model-changes \
      --context "${module}DbContext" \
      --project "src/Modules/${module}/${module}.Infrastructure" \
      --startup-project src/Host/TillKnowIT.Api >/dev/null 2>&1; then
    echo "ok       ${module}"
  else
    echo "PENDING  ${module}: model changed without a migration"
    status=1
  fi
done
exit $status
