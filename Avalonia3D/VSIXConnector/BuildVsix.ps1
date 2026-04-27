param(
    [string]$Configuration = "Debug",
    [string]$TargetFramework = "net472"
)

$ErrorActionPreference = "Stop"

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputDir = Join-Path $projectDir ("bin\{0}\{1}" -f $Configuration, $TargetFramework)
$plainOutputDir = Join-Path $projectDir ("bin\{0}" -f $Configuration)
$workDir = Join-Path $projectDir ("obj\{0}\vsix-package" -f $Configuration)

$dllPath = Join-Path $outputDir "ThreeDEngine.PreviewerVsix.dll"
if (-not (Test-Path $dllPath)) {
    throw "VSIX assembly was not found: $dllPath"
}

if (Test-Path $workDir) {
    Remove-Item $workDir -Recurse -Force
}
New-Item -ItemType Directory -Path $workDir | Out-Null

Copy-Item $dllPath (Join-Path $workDir "ThreeDEngine.PreviewerVsix.dll") -Force

$generatedPkgDef = Join-Path $projectDir ("obj\{0}\{1}\ThreeDEngine.PreviewerVsix.pkgdef" -f $Configuration, $TargetFramework)
$fallbackPkgDef = Join-Path $projectDir "ThreeDEngine.PreviewerVsix.pkgdef"
if (Test-Path $generatedPkgDef) {
    Copy-Item $generatedPkgDef (Join-Path $workDir "ThreeDEngine.PreviewerVsix.pkgdef") -Force
} elseif (Test-Path $fallbackPkgDef) {
    Copy-Item $fallbackPkgDef (Join-Path $workDir "ThreeDEngine.PreviewerVsix.pkgdef") -Force
} else {
    throw "Neither generated nor fallback pkgdef was found."
}

Copy-Item (Join-Path $projectDir "source.extension.vsixmanifest") (Join-Path $workDir "extension.vsixmanifest") -Force

$contentTypesXml = @'
<?xml version="1.0" encoding="utf-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="dll" ContentType="application/octet-stream" />
  <Default Extension="pkgdef" ContentType="text/plain" />
  <Default Extension="vsixmanifest" ContentType="text/xml" />
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
  <Override PartName="/extension.vsixmanifest" ContentType="text/xml" />
</Types>
'@
[System.IO.File]::WriteAllText((Join-Path $workDir "[Content_Types].xml"), $contentTypesXml, [System.Text.Encoding]::UTF8)

$relsDir = Join-Path $workDir "_rels"
New-Item -ItemType Directory -Path $relsDir | Out-Null
$relsXml = @'
<?xml version="1.0" encoding="utf-8"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.microsoft.com/developer/vsx-schema/2011" Target="extension.vsixmanifest" />
</Relationships>
'@
[System.IO.File]::WriteAllText((Join-Path $relsDir ".rels"), $relsXml, [System.Text.Encoding]::UTF8)

$vsixInTf = Join-Path $outputDir "ThreeDEngine.PreviewerVsix.vsix"
$vsixInDebug = Join-Path $plainOutputDir "ThreeDEngine.PreviewerVsix.vsix"
foreach ($target in @($vsixInTf, $vsixInDebug)) {
    if (Test-Path $target) {
        Remove-Item $target -Force
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($workDir, $vsixInTf)
Copy-Item $vsixInTf $vsixInDebug -Force

Write-Host "VSIX package created: $vsixInTf"
Write-Host "VSIX package copied to: $vsixInDebug"
