#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
source .local/dev.env
dotnet tool restore
ConnectionStrings__Database="$ProductConnection" dotnet ef database update --project src/ProductService
ConnectionStrings__Database="$IdentityConnection" dotnet ef database update --project src/IdentityService
ConnectionStrings__Database="$OrderConnection" dotnet ef database update --project src/OrderService
