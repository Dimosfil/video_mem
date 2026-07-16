$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$VenvPath = Join-Path $ProjectRoot ".venv"
$VenvPython = Join-Path $VenvPath "Scripts\python.exe"
$Requirements = Join-Path $ProjectRoot "requirements.txt"
$Stamp = Join-Path $VenvPath ".requirements-installed"

if (-not (Test-Path -LiteralPath $VenvPython)) {
    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($python) {
        & python -m venv $VenvPath
    }
    else {
        $pyLauncher = Get-Command py -ErrorAction SilentlyContinue
        if (-not $pyLauncher) {
            throw "Python is required, but no usable python or py command was found."
        }
        & py -m venv $VenvPath
    }
}

$needsInstall = -not (Test-Path -LiteralPath $Stamp)
if (-not $needsInstall) {
    $needsInstall = (Get-Item -LiteralPath $Requirements).LastWriteTimeUtc -gt (Get-Item -LiteralPath $Stamp).LastWriteTimeUtc
}

if ($needsInstall) {
    & $VenvPython -m pip install -r $Requirements
    New-Item -ItemType File -Force -Path $Stamp | Out-Null
}

& $VenvPython -m bot.main
