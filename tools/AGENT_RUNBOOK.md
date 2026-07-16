# Agent Runbook

Every command should be copy-pasteable from the project root.

## Install

```powershell
.\start.ps1
```

## Run

```powershell
.\start.ps1
```

## Test

```powershell
.\.venv\Scripts\python.exe -m unittest discover -s tests -v
```

## Build

```powershell
.\.venv\Scripts\python.exe -m py_compile app.py
```

## Smoke Check

```powershell
.\.venv\Scripts\python.exe -c "import app; print(app.default_download_directory())"
```

Expected result:

```text
The command imports the application and prints the project-local `downloads` path.
```

## Logs

```powershell
Get-ChildItem -LiteralPath .\downloads
```

## Environment Notes

- Windows desktop application; Tkinter is provided by the Python runtime.
- FFmpeg enables separate video/audio stream merging.
- Node.js is optional and is passed to yt-dlp when found on PATH.
- No application log file is written; status is shown in the UI.
