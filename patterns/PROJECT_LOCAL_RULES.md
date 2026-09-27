# Project Commands And Local Rules

## Common Commands

Install dependencies:

```powershell
.\start.ps1
```

Run:

```powershell
.\start.ps1
```

Run standalone YouTube viewer:

```powershell
.\youtube_viewer\start.ps1
```

Build standalone YouTube viewer executable:

```powershell
.\youtube_viewer\build.ps1
```

Test:

```powershell
.\.venv\Scripts\python.exe -m unittest discover -s tests -v
dotnet run --project .\youtube_viewer\tests\YouTubeViewer.Tests.csproj --configuration Release
```

Build:

```powershell
.\.venv\Scripts\python.exe -m py_compile app.py
dotnet build .\youtube_viewer\YouTubeViewer.csproj --configuration Release
```

Inspect logs:

```powershell
Get-ChildItem -LiteralPath .\downloads
```

## Working Areas

- Source: `app.py`
- Tests: `tests/`
- Standalone YouTube viewer: `youtube_viewer/`
- Standalone YouTube viewer tests: `youtube_viewer/tests/`
- Tools: `tools/` for durable development and agent tooling only
- Outputs/evidence/build artifacts: `downloads/`
- Summaries: `tools/summary/`
- Project memory: `tools/project-memory/`

## Local Rules

- After changes to YouTube Viewer code, UI, runtime configuration, or packaging,
  complete the relevant checks and build the Windows installer in the same task
  with `.\packaging\windows\build.ps1`, without waiting for a separate request.
  A standalone application EXE is not the final delivery artifact. Increment
  the patch version for a new installer unless the user specifies another
  version; keep project, executable, installer metadata, and documentation in
  sync. Verify the installer exists and its version matches the payload, record
  its SHA256, and include a clickable installer path in the final answer.
  Documentation-only changes do not require rebuilding. If packaging is
  blocked, report the exact blocker rather than claiming delivery is complete.
- Do not revert user changes unless explicitly requested.
- Treat dirty worktrees as normal.
- Keep changes scoped to the current task.
- Ask before destructive operations, broad formatting-only churn, dependency
  replacements, data migrations, public API or storage contract changes, or
  unrelated scope expansion.
- Treat this project root as the filesystem boundary for normal work unless the
  user gives an explicit concrete path and action.
- Before filesystem writes, verify the active project root and target identity
  from local instructions, README, manifests, git remote, service id, or project
  memory. If the task appears to target a different product, repository, or
  absolute path outside this root, stop and warn the user unless the current
  message explicitly authorizes that exact external path and action.
- Preserve text encodings when editing files.
- On Windows, never send Russian or other non-ASCII API/admin write bodies as a
  plain PowerShell `-Body` string. Prefer Node `fetch`, or send explicit UTF-8
  bytes with `charset=utf-8`, then read the saved value back and check for
  literal `????`, replacement characters, and mojibake fragments.
