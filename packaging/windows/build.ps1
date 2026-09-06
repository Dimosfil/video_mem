$ErrorActionPreference = "Stop"

$PackagingRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PackagingRoot "..\.."))
$AppRoot = Join-Path $RepoRoot "youtube_viewer"
$ProjectPath = Join-Path $AppRoot "YouTubeViewer.csproj"
$AppBuildScript = Join-Path $AppRoot "build.ps1"
$PayloadPath = Join-Path $AppRoot "dist\installer-payload"
$OutputPath = Join-Path $AppRoot "dist\installer"
$AppIcon = Join-Path $AppRoot "build\youtube-viewer.ico"
$InstallerScript = Join-Path $PackagingRoot "YouTubeViewer.iss"

if (-not (Test-Path -LiteralPath $ProjectPath -PathType Leaf)) {
    throw "YouTube Viewer project was not found: $ProjectPath"
}

[xml]$project = Get-Content -LiteralPath $ProjectPath -Raw
$version = [string](@($project.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version)
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "YouTube Viewer has an unsupported installer version: '$version'"
}

$compilerCandidates = @(
    $env:INNO_SETUP_COMPILER,
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if (-not $compiler) {
    throw "Inno Setup 6 compiler was not found. Install Inno Setup 6 or set INNO_SETUP_COMPILER."
}

& $AppBuildScript -SelfContained -OutputDirectory $PayloadPath
if ($LASTEXITCODE -ne 0) {
    throw "Self-contained YouTube Viewer build failed."
}

$payloadExecutable = Join-Path $PayloadPath "YouTube Viewer.exe"
if (-not (Test-Path -LiteralPath $payloadExecutable -PathType Leaf)) {
    throw "Installer payload executable is missing: $payloadExecutable"
}

$payloadVersion = (Get-Item -LiteralPath $payloadExecutable).VersionInfo.FileVersion
if ($payloadVersion -ne "$version.0") {
    throw "Payload version '$payloadVersion' does not match project version '$version'."
}

New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null

& $compiler `
    "/DAppVersion=$version" `
    "/DSourceDir=$PayloadPath" `
    "/DOutputDir=$OutputPath" `
    "/DAppIcon=$AppIcon" `
    $InstallerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed."
}

$installerPath = Join-Path $OutputPath "YouTube-Viewer-Setup-$version.exe"
if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "Installer compilation completed without the expected artifact: $installerPath"
}

$installer = Get-Item -LiteralPath $installerPath
$hash = Get-FileHash -LiteralPath $installerPath -Algorithm SHA256
Write-Output "Installer: $($installer.FullName)"
Write-Output "Version: $version"
Write-Output "Size: $($installer.Length) bytes"
Write-Output "SHA256: $($hash.Hash)"
