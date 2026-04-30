param(
    [string]$Configuration = "Debug",
    [string]$HostProject = "..\Avalonia3D.csproj",
    [string]$RoslynPath = ""
)

$ErrorActionPreference = "Stop"

Write-Host "Building PreviewerApp..." -ForegroundColor Cyan
$previewArgs = @(".\PreviewerApp\PreviewerApp.csproj", "-c", $Configuration, "-p:ThreeDEngineHostProject=$HostProject")
if ($RoslynPath -ne "") {
    $previewArgs += "-p:ThreeDEngineRoslynPath=$RoslynPath"
}
dotnet build @previewArgs

Write-Host "Running smoke tests..." -ForegroundColor Cyan
$smokeArgs = @("--project", ".\SmokeTests\ThreeDEngine.SmokeTests.csproj", "--", ".")
if ($RoslynPath -ne "") {
    dotnet run --project .\SmokeTests\ThreeDEngine.SmokeTests.csproj -p:ThreeDEngineRoslynPath="$RoslynPath" -- .
} else {
    dotnet run @smokeArgs
}

Write-Host "Build and smoke tests completed." -ForegroundColor Green
