#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
command -v dotnet >/dev/null 2>&1 || { echo "ERROR: dotnet is required." >&2; exit 127; }
dotnet restore Avalonia3D.Engine.sln --force-evaluate -p:RestorePackagesWithLockFile=true
project_count="$(find . -name '*.csproj' -not -path './Artifacts/*' -not -path '*/bin/*' -not -path '*/obj/*' | wc -l | tr -d '[:space:]')"
lock_count="$(find . -name packages.lock.json -not -path './Artifacts/*' -not -path '*/bin/*' -not -path '*/obj/*' | wc -l | tr -d '[:space:]')"
printf 'Generated %s package lock files:\n' "$lock_count"
find . -name packages.lock.json -not -path './Artifacts/*' -not -path '*/bin/*' -not -path '*/obj/*' | LC_ALL=C sort | sed 's#^#  #'
if [[ "$lock_count" != "$project_count" ]]; then
  echo "ERROR: expected $project_count package lock files, found $lock_count." >&2
  exit 4
fi
