# Technology Stack

Last reviewed: 2026-07-16

Canonical source: this file
Linked from: `README.md`, `tools/AGENT_RUNBOOK.md`

This is project documentation. Keep business rules, feature algorithms, workflow
contracts, state machines, and verification guarantees in project memory; keep
stack facts, commands, runtime assumptions, and operational notes here.

## Summary

- Primary stack: Python, Tkinter, yt-dlp, FFmpeg, Telegram Bot API
- Runtime model: local Windows desktop application plus a local or Dockerized Telegram polling process with bounded media workers
- Current confidence: verified from source, installed runtime, unit tests, Docker build, and credential-free Compose smoke checks

## Components

| Layer | Technology | Evidence | Notes |
| --- | --- | --- | --- |
| Language/runtime | Python 3.14.3 | `.venv`, `app.py` | Project virtual environment |
| Frontend | Tkinter/Tk 8.6 | `app.py` | Native Windows desktop UI |
| Media download | yt-dlp 2026.06.09 | `requirements.txt`, `app.py` | Metadata and single-video download |
| Media processing | FFmpeg 8.1.2 | runtime PATH, `README.md` | Merges separate video/audio streams |
| JavaScript runtime | Node.js | `app.py` | Optional YouTube challenge runtime for yt-dlp |
| Telegram API client | requests 2.x | `requirements.txt`, `bot/telegram.py` | Long polling, messages, callback buttons, and video delivery |
| Data/storage | Local filesystem and JSON job records | `app.py`, `bot/repository.py`, `.gitignore` | Downloads and bot runtime data are ignored |
| Build/package | PowerShell + venv/pip | `start.ps1` | No distributable installer yet |
| Bot container | Docker/Compose, Python 3.13 slim | `Dockerfile`, `compose.yaml` | Includes FFmpeg and Node.js; publishes no ports |
| Test/quality | stdlib `unittest`, `py_compile` | `tests/` | Focused unit and import/compile checks |
| Deployment/runtime | Local Windows processes or Docker Compose | `start.ps1`, `start-bot.ps1`, `compose.yaml` | Bot MVP needs only a Telegram token and outbound network access |

## Commands

| Purpose | Command | Evidence |
| --- | --- | --- |
| Install | `.\start.ps1` | `start.ps1` |
| Run | `.\start.ps1` | `README.md` |
| Test | `.\.venv\Scripts\python.exe -m unittest discover -s tests -v` | `README.md`, `tests/` |
| Compile check | `.\.venv\Scripts\python.exe -m py_compile app.py` | `app.py` |
| Run Telegram MVP | `.\start-bot.ps1` | `start-bot.ps1`, `.env.bot.example` |
| Run Telegram MVP in Docker | `docker compose up -d --build` | `Dockerfile`, `compose.yaml` |

## External Services

| Service | Role | Evidence | Boundary |
| --- | --- | --- | --- |
| YouTube and other yt-dlp-supported sites | Remote media metadata and files | `yt-dlp` extractors | Network calls occur only after a user submits a URL |
| Telegram Bot API | Bot updates, messages, callback buttons, video delivery | `bot/telegram.py` | Enabled only with an environment token |

## Gaps

- No installer or packaged executable.
- No automated network integration test.
- Real Telegram delivery still requires a bot token and network verification.
- The MVP uses an in-process bounded queue; it does not yet survive a process restart while a job is running.
- No multi-clip editing, vertical crop profiles, captions, or platform-specific adaptation beyond Telegram size/codec normalization.
