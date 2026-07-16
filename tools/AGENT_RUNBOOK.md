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

Telegram video clip MVP:

```powershell
Copy-Item .env.bot.example .env
.\start-bot.ps1
```

Telegram video clip MVP in Docker:

```powershell
docker compose up -d --build
docker compose ps
docker compose logs --tail 100 video-clip-bot
```

Stop only this project stack:

```powershell
docker compose down
```

## Test

```powershell
.\.venv\Scripts\python.exe -m unittest discover -s tests -v
```

## Build

```powershell
.\.venv\Scripts\python.exe -m compileall -q app.py bot
```

## Smoke Check

```powershell
.\.venv\Scripts\python.exe -c "import app; print(app.default_download_directory())"
```

Expected result:

```text
The command imports the application and prints the project-local `downloads` path.
```

Credential-free Telegram bot startup check:

```powershell
$env:TELEGRAM_POLLING_ENABLED = "false"
.\.venv\Scripts\python.exe -m bot.main
```

Expected result: configuration and imports load successfully, then the process exits because polling is disabled.

Credential-free Docker image check (does not use `.env` or contact Telegram):

```powershell
docker build --tag video-mem-bot:smoke .
docker run --rm --env TELEGRAM_POLLING_ENABLED=false video-mem-bot:smoke
```

Expected result: the image contains Python, yt-dlp, FFmpeg, and Node.js; the bot loads configuration and exits because polling is disabled.

## Logs

```powershell
Get-ChildItem -LiteralPath .\downloads
```

## Environment Notes

- Windows desktop application; Tkinter is provided by the Python runtime.
- FFmpeg enables separate video/audio stream merging.
- Node.js is optional and is passed to yt-dlp when found on PATH.
- The Telegram MVP is chat-only and does not require a site, domain, inbound port, or public HTTPS URL.
- Real Telegram polling requires `TELEGRAM_BOT_TOKEN` and outbound network access.
- Docker Compose publishes no ports, restarts the bot unless stopped, and persists `/app/data` in project-local `./data`.
- For Docker cookies, place the file under `./data` and configure its container path under `/app/data`.
- Telegram secrets belong only in ignored `.env` or deployment secret storage.
- No application log file is written; desktop status is shown in the UI and bot status is written to standard output.
