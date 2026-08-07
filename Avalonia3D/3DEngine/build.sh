#!/usr/bin/env bash
set -euo pipefail

engine_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
configuration="Release"
skip_benchmarks=false

for argument in "$@"; do
  case "$argument" in
    Debug|Release) configuration="$argument" ;;
    --skip-benchmarks) skip_benchmarks=true ;;
    *) echo "ERROR: unsupported argument '$argument'." >&2; exit 2 ;;
  esac
done

cd "$engine_root"
artifact_root="$engine_root/Artifacts"
rm -rf "$artifact_root"
mkdir -p "$artifact_root/TestResults" "$artifact_root/Packages" "$artifact_root/Baseline/CPU"

command -v dotnet >/dev/null 2>&1 || { echo "ERROR: the SDK pinned by global.json is required, but 'dotnet' was not found." >&2; exit 127; }
command -v node >/dev/null 2>&1 || { echo "ERROR: Node.js is required for WebGL and manifest validation." >&2; exit 127; }

dotnet --version > "$artifact_root/dotnet-version.txt"
dotnet --info > "$artifact_root/dotnet-info.txt"
node --version > "$artifact_root/node-version.txt"

expected_sdk="$(node -p "require('./global.json').sdk.version")"
actual_sdk="$(dotnet --version)"
if [[ -n "$expected_sdk" && "$actual_sdk" != "$expected_sdk" ]]; then
  echo "ERROR: global.json requires .NET SDK $expected_sdk, actual SDK is $actual_sdk." >&2
  exit 3
fi
expected_node="$(tr -d '[:space:]' < .node-version)"
actual_node="$(node --version | sed 's/^v//')"
if [[ -n "$expected_node" && "$actual_node" != "$expected_node" ]]; then
  echo "ERROR: .node-version requires Node.js $expected_node, actual Node.js is $actual_node." >&2
  exit 3
fi

node eng/validate-csharp-source.mjs

project_count="$(find . -name '*.csproj' -not -path './Artifacts/*' -not -path '*/bin/*' -not -path '*/obj/*' | wc -l | tr -d '[:space:]')"
lock_count="$(find . -name packages.lock.json -not -path './Artifacts/*' -not -path '*/bin/*' -not -path '*/obj/*' | wc -l | tr -d '[:space:]')"
if [[ "$lock_count" == "$project_count" ]]; then
  dotnet restore Avalonia3D.Engine.sln --locked-mode -p:LockedRestore=true
else
  echo "WARNING: package lock set is incomplete; bootstrapping exact lock files. Commit the generated packages.lock.json files." >&2
  dotnet restore Avalonia3D.Engine.sln --force-evaluate -p:RestorePackagesWithLockFile=true
  dotnet restore Avalonia3D.Engine.sln --locked-mode -p:LockedRestore=true
fi

dotnet build Avalonia3D.Engine.sln --configuration "$configuration" --no-restore
dotnet build Avalonia3D.Engine.csproj --configuration "$configuration" --framework net8.0-browser --no-restore
node eng/validate-package-architecture.mjs
node eng/validate-baseline.mjs
node --check WebGL/mini3d.webgl.js
node eng/webgl-runtime.mjs

dotnet test Tests/Avalonia3D.Engine.Tests.csproj \
  --configuration "$configuration" \
  --no-build \
  --logger "trx;LogFileName=engine-tests.trx" \
  --results-directory "$artifact_root/TestResults" \
  --collect "XPlat Code Coverage"

dotnet run --project Tools/ApiSnapshot/Avalonia3D.Engine.ApiSnapshot.csproj \
  --configuration "$configuration" --no-build -- "$artifact_root/public-api.txt"

if [[ "$skip_benchmarks" == false ]]; then
  dotnet run --project Benchmarks/Avalonia3D.Engine.Benchmarks.csproj \
    --configuration "$configuration" --no-build -- \
    --output "$artifact_root/Baseline/CPU" \
    --policy "$engine_root/Baselines/baseline-policy.json" \
    --result-name "cpu-current"
fi

package_projects=(
  Avalonia3D.Core.csproj
  Avalonia3D.Assets.Gltf.csproj
  Avalonia3D.Physics.Jitter2.csproj
  Avalonia3D.Avalonia.csproj
  Avalonia3D.OpenGL.csproj
  Avalonia3D.WebGL.csproj
  Avalonia3D.Editor.csproj
  Avalonia3D.Engine.csproj
)
for package_project in "${package_projects[@]}"; do
  dotnet pack "$package_project" \
    --configuration "$configuration" \
    --no-build \
    --output "$artifact_root/Packages"
done

node eng/source-manifest.mjs "$artifact_root/source-manifest.sha256"

echo "Avalonia3D Engine build gate completed successfully."
