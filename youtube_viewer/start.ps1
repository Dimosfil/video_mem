$ErrorActionPreference = "Stop"

$AppRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$Executable = Join-Path $AppRoot "dist\YouTube Viewer\YouTube Viewer.exe"
$SourcePatterns = @("*.cs", "*.xaml", "*.csproj")
$SourceFiles = foreach ($pattern in $SourcePatterns) {
    Get-ChildItem -LiteralPath $AppRoot -Filter $pattern -File
}
$NeedsBuild = -not (Test-Path -LiteralPath $Executable -PathType Leaf)

if (-not $NeedsBuild) {
    $executableTime = (Get-Item -LiteralPath $Executable).LastWriteTimeUtc
    $NeedsBuild = @($SourceFiles | Where-Object { $_.LastWriteTimeUtc -gt $executableTime }).Count -gt 0
}

if ($NeedsBuild) {
    & (Join-Path $AppRoot "build.ps1")
}

if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) {
    throw "YouTube Viewer executable is missing after build: $Executable"
}

Start-Process -FilePath $Executable -WorkingDirectory (Split-Path -Parent $Executable)
