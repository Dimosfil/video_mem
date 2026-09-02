$ErrorActionPreference = "Stop"

$AppRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$VenvPath = Join-Path $AppRoot ".venv"
$VenvPython = Join-Path $VenvPath "Scripts\python.exe"
$BuildRequirements = Join-Path $AppRoot "build-requirements.txt"
$IconSource = Join-Path $AppRoot "assets\youtube-viewer-reference.png"
$IconPath = Join-Path $AppRoot "build\youtube-viewer.ico"
$Project = Join-Path $AppRoot "YouTubeViewer.csproj"
$DistPath = Join-Path $AppRoot "dist"
$PublishPath = Join-Path $DistPath "YouTube Viewer"
$Executable = Join-Path $PublishPath "YouTube Viewer.exe"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET 8 SDK is required, but dotnet was not found."
}

if (-not (Test-Path -LiteralPath $VenvPython)) {
    if (Get-Command python -ErrorAction SilentlyContinue) {
        & python -m venv $VenvPath
    }
    elseif (Get-Command py -ErrorAction SilentlyContinue) {
        & py -m venv $VenvPath
    }
    elseif (Get-Command uv -ErrorAction SilentlyContinue) {
        & uv venv $VenvPath
    }
    else {
        throw "Python is required, but no usable python, py, or uv command was found."
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create the icon-build Python environment."
    }
}

& $VenvPython -m pip install -r $BuildRequirements
if ($LASTEXITCODE -ne 0) {
    throw "Failed to install icon-build requirements."
}

& $VenvPython (Join-Path $AppRoot "build_support\icon_builder.py") $IconSource $IconPath
if ($LASTEXITCODE -ne 0) {
    throw "Failed to build the application icon."
}

$runningViewer = @(Get-CimInstance Win32_Process -Filter "Name = 'YouTube Viewer.exe'" | Where-Object {
    $_.ExecutablePath -and [System.IO.Path]::GetFullPath($_.ExecutablePath) -eq [System.IO.Path]::GetFullPath($Executable)
})
if ($runningViewer.Count -gt 0) {
    throw "Close YouTube Viewer before rebuilding it."
}

$fullAppRoot = [System.IO.Path]::GetFullPath($AppRoot).TrimEnd('\')
$fullPublishPath = [System.IO.Path]::GetFullPath($PublishPath).TrimEnd('\')
if (-not $fullPublishPath.StartsWith($fullAppRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe publish path: $fullPublishPath"
}

if (Test-Path -LiteralPath $fullPublishPath) {
    [System.IO.Directory]::Delete($fullPublishPath, $true)
}

& dotnet restore $Project --runtime win-x64 --locked-mode --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Locked NuGet restore failed."
}

& dotnet publish $Project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --output $PublishPath `
    --no-restore `
    --nologo `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw ".NET publish failed."
}

if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) {
    throw "Build completed without the expected executable: $Executable"
}

Write-Output $Executable
