# Technology Stack

Last reviewed: 2026-07-15

Canonical source: this file
Linked from: `README.md`, `tools/AGENT_RUNBOOK.md`

This is project documentation. Keep business rules, feature algorithms, workflow
contracts, state machines, and verification guarantees in project memory; keep
stack facts, commands, runtime assumptions, and operational notes here.

## Summary

- Primary stack: Python, Tkinter, yt-dlp, FFmpeg
- Runtime model: local single-process Windows desktop application with background worker threads
- Current confidence: verified from source, installed runtime, and unit tests

## Components

| Layer | Technology | Evidence | Notes |
| --- | --- | --- | --- |
| Language/runtime | Python 3.14.3 | `.venv`, `app.py` | Project virtual environment |
| Frontend | Tkinter/Tk 8.6 | `app.py` | Native Windows desktop UI |
| Media download | yt-dlp 2026.06.09 | `requirements.txt`, `app.py` | Metadata and single-video download |
| Media processing | FFmpeg 8.1.2 | runtime PATH, `README.md` | Merges separate video/audio streams |
| JavaScript runtime | Node.js | `app.py` | Optional YouTube challenge runtime for yt-dlp |
| Data/storage | Local filesystem | `app.py`, `.gitignore` | Downloads are ignored; no database |
| Build/package | PowerShell + venv/pip | `start.ps1` | No distributable installer yet |
| Test/quality | stdlib `unittest`, `py_compile` | `tests/` | Focused unit and import/compile checks |
| Deployment/runtime | Local Windows process | `start.ps1` | No server or bot runtime yet |

## Commands

| Purpose | Command | Evidence |
| --- | --- | --- |
| Install | `.\start.ps1` | `start.ps1` |
| Run | `.\start.ps1` | `README.md` |
| Test | `.\.venv\Scripts\python.exe -m unittest discover -s tests -v` | `README.md`, `tests/` |
| Compile check | `.\.venv\Scripts\python.exe -m py_compile app.py` | `app.py` |

## External Services

| Service | Role | Evidence | Boundary |
| --- | --- | --- | --- |
| YouTube and other yt-dlp-supported sites | Remote media metadata and files | `yt-dlp` extractors | Network calls occur only after a user submits a URL |

## Gaps

- No installer or packaged executable.
- No automated network integration test.
- No bot transport, queue, clipping, compression, or platform-size adaptation yet.
