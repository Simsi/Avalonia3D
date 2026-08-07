param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $SkipBenchmarks
)

$ErrorActionPreference = 'Stop'
$engineRoot = $PSScriptRoot
$artifactRoot = Join-Path $engineRoot 'Artifacts'
$testResultsPath = Join-Path $artifactRoot 'TestResults'
$packagesPath = Join-Path $artifactRoot 'Packages'
$cpuBaselinePath = Join-Path $artifactRoot 'Baseline/CPU'
Set-Location $engineRoot

function Assert-NativeSuccess([string] $Operation, [int] $ExitCode) {
    if ($ExitCode -ne 0) {
        throw "$Operation failed with exit code $ExitCode."
    }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'The SDK pinned by global.json is required, but dotnet was not found.' }
if (-not (Get-Command node -ErrorAction SilentlyContinue)) { throw 'Node.js is required for WebGL and manifest validation.' }

Remove-Item -Recurse -Force $artifactRoot -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $testResultsPath | Out-Null
New-Item -ItemType Directory -Force -Path $packagesPath | Out-Null
New-Item -ItemType Directory -Force -Path $cpuBaselinePath | Out-Null

$dotnetVersionOutput = & dotnet --version
Assert-NativeSuccess 'dotnet --version' $LASTEXITCODE
$dotnetVersionOutput | Set-Content -Encoding UTF8 (Join-Path $artifactRoot 'dotnet-version.txt')
$dotnetInfoOutput = & dotnet --info
Assert-NativeSuccess 'dotnet --info' $LASTEXITCODE
$dotnetInfoOutput | Set-Content -Encoding UTF8 (Join-Path $artifactRoot 'dotnet-info.txt')
$nodeVersionOutput = & node --version
Assert-NativeSuccess 'node --version' $LASTEXITCODE
$nodeVersionOutput | Set-Content -Encoding UTF8 (Join-Path $artifactRoot 'node-version.txt')

$expectedSdk = (Get-Content global.json -Raw | ConvertFrom-Json).sdk.version
$actualSdk = ($dotnetVersionOutput | Select-Object -First 1).Trim()
if ($actualSdk -ne $expectedSdk) { throw "global.json requires .NET SDK $expectedSdk, actual SDK is $actualSdk." }
$expectedNode = (Get-Content .node-version -Raw).Trim()
$actualNode = (($nodeVersionOutput | Select-Object -First 1).Trim() -replace '^v', '')
if ($actualNode -ne $expectedNode) { throw ".node-version requires Node.js $expectedNode, actual Node.js is $actualNode." }

& node eng/validate-csharp-source.mjs
Assert-NativeSuccess 'C# source validation' $LASTEXITCODE

$projectFiles = @(Get-ChildItem -Path $engineRoot -Filter *.csproj -Recurse | Where-Object { $_.FullName -notmatch '[\\/](Artifacts|bin|obj)[\\/]' })
$lockFiles = @(Get-ChildItem -Path $engineRoot -Filter packages.lock.json -Recurse | Where-Object { $_.FullName -notmatch '[\\/](Artifacts|bin|obj)[\\/]' })
if ($lockFiles.Count -eq $projectFiles.Count) {
    & dotnet restore Avalonia3D.Engine.sln --locked-mode -p:LockedRestore=true
    Assert-NativeSuccess 'locked NuGet restore' $LASTEXITCODE
} else {
    Write-Warning 'Package lock set is incomplete; bootstrapping exact lock files. Commit the generated packages.lock.json files.'
    & dotnet restore Avalonia3D.Engine.sln --force-evaluate -p:RestorePackagesWithLockFile=true
    Assert-NativeSuccess 'NuGet lock bootstrap' $LASTEXITCODE
    & dotnet restore Avalonia3D.Engine.sln --locked-mode -p:LockedRestore=true
    Assert-NativeSuccess 'locked NuGet restore after bootstrap' $LASTEXITCODE
}

& dotnet build Avalonia3D.Engine.sln --configuration $Configuration --no-restore
Assert-NativeSuccess 'solution build' $LASTEXITCODE
& dotnet build Avalonia3D.Engine.csproj --configuration $Configuration --framework net8.0-browser --no-restore
Assert-NativeSuccess 'browser target build' $LASTEXITCODE
& node eng/validate-package-architecture.mjs
Assert-NativeSuccess 'package architecture validation' $LASTEXITCODE
& node eng/validate-baseline.mjs
Assert-NativeSuccess 'baseline contract validation' $LASTEXITCODE
& node --check WebGL/mini3d.webgl.js
Assert-NativeSuccess 'WebGL syntax validation' $LASTEXITCODE
& node eng/webgl-runtime.mjs
Assert-NativeSuccess 'WebGL embedded-module validation' $LASTEXITCODE

& dotnet test Tests/Avalonia3D.Engine.Tests.csproj `
    --configuration $Configuration `
    --no-build `
    --logger 'trx;LogFileName=engine-tests.trx' `
    --results-directory $testResultsPath `
    --collect 'XPlat Code Coverage'
Assert-NativeSuccess 'regression tests' $LASTEXITCODE

$apiSnapshotPath = Join-Path $artifactRoot 'public-api.txt'
& dotnet run --project Tools/ApiSnapshot/Avalonia3D.Engine.ApiSnapshot.csproj `
    --configuration $Configuration --no-build -- $apiSnapshotPath
Assert-NativeSuccess 'public API snapshot' $LASTEXITCODE

if (-not $SkipBenchmarks) {
    & dotnet run --project Benchmarks/Avalonia3D.Engine.Benchmarks.csproj `
        --configuration $Configuration --no-build -- `
        --output $cpuBaselinePath `
        --policy (Join-Path $engineRoot 'Baselines/baseline-policy.json') `
        --result-name 'cpu-current'
    Assert-NativeSuccess 'CPU baseline validation' $LASTEXITCODE
}

$packageProjects = @(
    'Avalonia3D.Core.csproj',
    'Avalonia3D.Assets.Gltf.csproj',
    'Avalonia3D.Physics.Jitter2.csproj',
    'Avalonia3D.Avalonia.csproj',
    'Avalonia3D.OpenGL.csproj',
    'Avalonia3D.WebGL.csproj',
    'Avalonia3D.Editor.csproj',
    'Avalonia3D.Engine.csproj'
)
foreach ($packageProject in $packageProjects) {
    & dotnet pack $packageProject `
        --configuration $Configuration `
        --no-build `
        --output $packagesPath
    Assert-NativeSuccess "NuGet package creation: $packageProject" $LASTEXITCODE
}

& node eng/source-manifest.mjs (Join-Path $artifactRoot 'source-manifest.sha256')
Assert-NativeSuccess 'source manifest generation' $LASTEXITCODE

Write-Host 'Avalonia3D Engine build gate completed successfully.'
