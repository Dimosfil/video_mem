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

Standalone YouTube viewer:

```powershell
.\youtube_viewer\start.ps1
```

The launcher starts `youtube_viewer/dist/YouTube Viewer/YouTube Viewer.exe` and
builds it first when the executable is missing or viewer source is newer.

The viewer fails closed unless the Happ HTTP proxy is reachable. Its default is
`http://127.0.0.1:10809`; set `YOUTUBE_VIEWER_PROXY` before launch only when the
local Happ HTTP port has been changed.

The WebView2 user-data root is `%LOCALAPPDATA%\VideoMem\YouTubeViewer`. Build,
publish, and executable replacement operations must preserve it. Pass this root
to WebView2 directly; WebView2 appends its own `EBWebView` directory.

## Test

```powershell
.\.venv\Scripts\python.exe -m unittest discover -s tests -v
dotnet run --project .\youtube_viewer\tests\YouTubeViewer.Tests.csproj --configuration Release
```

## Build

```powershell
.\.venv\Scripts\python.exe -m compileall -q app.py bot
dotnet build .\youtube_viewer\YouTubeViewer.csproj --configuration Release
.\youtube_viewer\build.ps1
```

## Smoke Check

```powershell
.\.venv\Scripts\python.exe -c "import app; print(app.default_download_directory())"
Get-Item -LiteralPath '.\youtube_viewer\dist\YouTube Viewer\YouTube Viewer.exe'
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

- Two Windows desktop entry points are available: root Python downloader
  `app.py` and the independent .NET 8 WPF `youtube_viewer` application.
- The viewer requires .NET 8 Desktop Runtime and Microsoft Edge WebView2
  Runtime and does not use yt-dlp.
- The packaged viewer runs as `YouTube Viewer.exe` with AppUserModelID
  `Dimosfil.VideoMem.YouTubeViewer`; it must not be launched through Python for
  normal use.
- The viewer provides real WebView2 tabs, browser navigation, address/search,
  loading state, tab restoration, and browser-style keyboard shortcuts.
- WebView2 is explicitly configured to use the Happ HTTP proxy at
  `127.0.0.1:10809`, with QUIC disabled and no direct-connect fallback when the
  proxy is unavailable.
- Tkinter is provided by the Python runtime.
- FFmpeg enables separate video/audio stream merging.
- Node.js is optional and is passed to yt-dlp when found on PATH.
- The Telegram MVP is chat-only and does not require a site, domain, inbound port, or public HTTPS URL.
- Real Telegram polling requires `TELEGRAM_BOT_TOKEN` and outbound network access.
- Docker Compose publishes no ports, restarts the bot unless stopped, and persists `/app/data` in project-local `./data`.
- For Docker cookies, place the file under `./data` and configure its container path under `/app/data`.
- Telegram secrets belong only in ignored `.env` or deployment secret storage.
- No application log file is written; desktop status is shown in the UI and bot status is written to standard output.
