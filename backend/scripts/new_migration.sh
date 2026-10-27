#!/usr/bin/env bash
# Add a migration for one module: scripts/new_migration.sh <Module> [Name] [--reset]
#   --reset  throws away the module's migrations and recreates Initial. Only for modules nobody has applied to a database yet.
set -euo pipefail
cd "$(dirname "$0")/.."
module="$1"; name="${2:-Initial}"; reset="${3:-}"
project="src/Modules/${module}/${module}.Infrastructure"
if [ "$reset" = "--reset" ] || [ "${2:-}" = "--reset" ]; then
  rm -rf "${project}/Persistence/Migrations"
  name="Initial"
fi
dotnet ef migrations add "$name" --context "${module}DbContext" --project "$project" \
  --startup-project src/Host/TillKnowIT.Api --output-dir Persistence/Migrations
