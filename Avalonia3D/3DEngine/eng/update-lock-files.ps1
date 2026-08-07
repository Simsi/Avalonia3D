$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'dotnet is required.' }
& dotnet restore Avalonia3D.Engine.sln --force-evaluate -p:RestorePackagesWithLockFile=true
if ($LASTEXITCODE -ne 0) { throw "NuGet lock refresh failed with exit code $LASTEXITCODE." }
$projects = @(Get-ChildItem -Path $root -Filter *.csproj -Recurse | Where-Object { $_.FullName -notmatch '[\/](Artifacts|bin|obj)[\/]' })
$locks = @(Get-ChildItem -Path $root -Filter packages.lock.json -Recurse | Where-Object { $_.FullName -notmatch '[\/](Artifacts|bin|obj)[\/]' })
Write-Host "Generated $($locks.Count) package lock files:"
$locks | Sort-Object FullName | ForEach-Object { Write-Host "  $($_.FullName)" }
if ($locks.Count -ne $projects.Count) { throw "Expected $($projects.Count) package lock files, found $($locks.Count)." }
