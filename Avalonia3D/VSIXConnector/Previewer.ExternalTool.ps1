param(
    [Parameter(Mandatory=$true)][string]$ProjectPath,
    [Parameter(Mandatory=$true)][string]$ItemPath,
    [string]$Configuration = "Debug",
    [string]$PreviewerProject = ""
)

$ErrorActionPreference = "Stop"

function Find-RepoRoot([string]$start) {
    $dir = Split-Path -Parent (Resolve-Path $start)
    while ($dir) {
        if (Test-Path (Join-Path $dir "PreviewerApp\PreviewerApp.csproj")) { return $dir }
        $parent = Split-Path -Parent $dir
        if ($parent -eq $dir) { break }
        $dir = $parent
    }
    throw "Cannot locate PreviewerApp\PreviewerApp.csproj above '$start'."
}

function Resolve-TypeName([string]$filePath) {
    $text = [System.IO.File]::ReadAllText($filePath)
    $namespace = ""
    $nsMatch = [regex]::Match($text, 'namespace\s+([A-Za-z_][A-Za-z0-9_.]*)\s*[;{]')
    if ($nsMatch.Success) { $namespace = $nsMatch.Groups[1].Value }

    $classMatches = [regex]::Matches($text, '(?m)(?:public|internal|private|protected|sealed|abstract|partial|static|new|\s)*\s*(?:class|record\s+class)\s+([A-Za-z_][A-Za-z0-9_]*)(?:\s*<[^>]+>)?')
    if ($classMatches.Count -eq 0) {
        throw "No class or record class declaration found in '$filePath'."
    }

    $name = $classMatches[0].Groups[1].Value
    if ([string]::IsNullOrWhiteSpace($namespace)) { return $name }
    return "$namespace.$name"
}

$repoRoot = Find-RepoRoot $ProjectPath
if ([string]::IsNullOrWhiteSpace($PreviewerProject)) {
    $PreviewerProject = Join-Path $repoRoot "PreviewerApp\PreviewerApp.csproj"
}

$typeName = Resolve-TypeName $ItemPath

Write-Host "Building host project: $ProjectPath"
dotnet build $ProjectPath -c $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Building previewer: $PreviewerProject"
dotnet build $PreviewerProject -c $Configuration -p:ThreeDEngineHostProject=$ProjectPath
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$projectDir = Split-Path -Parent (Resolve-Path $ProjectPath)
$tfmDirs = Get-ChildItem -Path (Join-Path $projectDir "bin\$Configuration") -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending
if ($tfmDirs.Count -eq 0) { throw "Cannot locate host output under bin\$Configuration." }

$projectName = [System.IO.Path]::GetFileNameWithoutExtension($ProjectPath)
$assembly = $null
foreach ($dir in $tfmDirs) {
    $candidate = Join-Path $dir.FullName "$projectName.dll"
    if (Test-Path $candidate) { $assembly = $candidate; break }
}
if (-not $assembly) { throw "Cannot locate host assembly '$projectName.dll'." }

$previewerDir = Split-Path -Parent (Resolve-Path $PreviewerProject)
$previewerDll = Get-ChildItem -Path (Join-Path $previewerDir "bin\$Configuration") -Filter "PreviewerApp.dll" -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $previewerDll) { throw "Cannot locate PreviewerApp.dll." }

Write-Host "Opening 3D preview: $typeName"
dotnet $previewerDll.FullName --assembly $assembly --type $typeName --project $ProjectPath
