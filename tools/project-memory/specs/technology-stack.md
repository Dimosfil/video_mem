# Technology Stack

Last reviewed: 2026-09-02

Canonical source: this file
Linked from: `README.md`, `tools/AGENT_RUNBOOK.md`

This is project documentation. Keep business rules, feature algorithms, workflow
contracts, state machines, and verification guarantees in project memory; keep
stack facts, commands, runtime assumptions, and operational notes here.

## Summary

- Primary stack: Python, Tkinter, yt-dlp, FFmpeg, Telegram Bot API, .NET 8, WPF, Edge WebView2
- Runtime model: separate Windows downloader and viewer desktop processes plus a local or Dockerized Telegram polling process with bounded media workers
- Current confidence: verified from source, installed runtimes, unit tests, viewer UI/network smoke checks, Docker build, and credential-free Compose smoke checks

## Components

| Layer | Technology | Evidence | Notes |
| --- | --- | --- | --- |
| Language/runtime | Python 3.14.3 | `.venv`, `app.py` | Project virtual environment |
| Downloader frontend | Tkinter/Tk 8.6 | `app.py` | Native Windows download UI |
| Viewer frontend | .NET 8 WPF + Edge WebView2 1.0.4191.47 | `youtube_viewer/YouTubeViewer.csproj`, `youtube_viewer/MainWindow.xaml` | Tabs, navigation, shortcuts, downloads disabled |
| Media download | yt-dlp 2026.06.09 | `requirements.txt`, `app.py` | Metadata and single-video download |
| Media processing | FFmpeg 8.1.2 | runtime PATH, `README.md` | Merges separate video/audio streams |
| JavaScript runtime | Node.js | `app.py` | Optional YouTube challenge runtime for yt-dlp |
| Telegram API client | requests 2.x | `requirements.txt`, `bot/telegram.py` | Long polling, messages, callback buttons, and video delivery |
| Data/storage | Local filesystem, JSON job records, WebView profile | `app.py`, `bot/repository.py`, `youtube_viewer/MainWindow.xaml.cs`, `.gitignore` | Downloads, bot runtime data, and private viewer state are ignored |
| Build/package | PowerShell, venv/pip, dotnet publish, NuGet lock file | `start.ps1`, `youtube_viewer/build.ps1`, `youtube_viewer/start.ps1` | Viewer produces a framework-dependent win-x64 desktop bundle; no installer yet |
| Bot container | Docker/Compose, Python 3.13 slim | `Dockerfile`, `compose.yaml` | Includes FFmpeg and Node.js; publishes no ports |
| Test/quality | Python `unittest` + .NET executable tests | `tests/`, `youtube_viewer/tests/` | Downloader regression and viewer address/proxy/tab-state checks |
| Deployment/runtime | Local Windows processes or Docker Compose | `start.ps1`, `start-bot.ps1`, `youtube_viewer/start.ps1`, `compose.yaml` | Bot MVP needs a Telegram token and outbound network access; viewer needs local Happ |

## Commands

| Purpose | Command | Evidence |
| --- | --- | --- |
| Install | `.\start.ps1` | `start.ps1` |
| Run | `.\start.ps1` | `README.md` |
| Run YouTube viewer | `.\youtube_viewer\start.ps1` | `youtube_viewer/README.md` |
| Build YouTube viewer | `.\youtube_viewer\build.ps1` | `youtube_viewer/build.ps1` |
| Test | `.\.venv\Scripts\python.exe -m unittest discover -s tests -v` | `README.md`, `tests/` |
| Test YouTube viewer | `dotnet run --project .\youtube_viewer\tests\YouTubeViewer.Tests.csproj --configuration Release` | `youtube_viewer/README.md`, `youtube_viewer/tests/` |
| Compile check | `.\.venv\Scripts\python.exe -m compileall -q app.py bot` | `app.py`, `bot/` |
| Run Telegram MVP | `.\start-bot.ps1` | `start-bot.ps1`, `.env.bot.example` |
| Run Telegram MVP in Docker | `docker compose up -d --build` | `Dockerfile`, `compose.yaml` |
| Viewer compile check | `dotnet build .\youtube_viewer\YouTubeViewer.csproj --configuration Release` | `youtube_viewer/` |

## External Services

| Service | Role | Evidence | Boundary |
| --- | --- | --- | --- |
| YouTube and other yt-dlp-supported sites | Remote media metadata and files | `yt-dlp` extractors | Network calls occur only after a user submits a URL |
| Telegram Bot API | Bot updates, messages, callback buttons, video delivery | `bot/telegram.py` | Enabled only with an environment token |

## Gaps

- No installer; the verified packaged executable is produced under `youtube_viewer/dist/`.
- No automated network integration test.
- Real Telegram delivery still requires a bot token and network verification.
- The MVP uses an in-process bounded queue; it does not yet survive a process restart while a job is running.
- No multi-clip editing, vertical crop profiles, captions, or platform-specific adaptation beyond Telegram size/codec normalization.
