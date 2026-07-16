$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$VenvPath = Join-Path $ProjectRoot ".venv"
$VenvPython = Join-Path $VenvPath "Scripts\python.exe"
$Requirements = Join-Path $ProjectRoot "requirements.txt"
$Stamp = Join-Path $VenvPath ".requirements-installed"

function Invoke-HostPython {
    param([string[]]$Arguments)

    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($python) {
        & python @Arguments
        return
    }

    $pyLauncher = Get-Command py -ErrorAction SilentlyContinue
    if ($pyLauncher) {
        & py @Arguments
        return
    }

    $uv = Get-Command uv -ErrorAction SilentlyContinue
    if ($uv) {
        & uv venv $VenvPath
        return
    }

    throw "Python is required, but no usable python, py, or uv command was found."
}

if (-not (Test-Path -LiteralPath $VenvPython)) {
    Invoke-HostPython -Arguments @("-m", "venv", $VenvPath)
}

$needsInstall = -not (Test-Path -LiteralPath $Stamp)
if (-not $needsInstall) {
    $needsInstall = (Get-Item -LiteralPath $Requirements).LastWriteTimeUtc -gt (Get-Item -LiteralPath $Stamp).LastWriteTimeUtc
}

if ($needsInstall) {
    & $VenvPython -m pip install --upgrade pip
    & $VenvPython -m pip install -r $Requirements
    New-Item -ItemType File -Force -Path $Stamp | Out-Null
}

& $VenvPython (Join-Path $ProjectRoot "app.py")
