# Pending Tasks

Use this file for active project-wide plans and multi-step work.

Keep entries concise and task-relevant. Do not store full diffs, large logs,
generated outputs, secrets, credentials, or private production data.

## Status Markers

- `[ ]` not started
- `[~]` in progress
- `[x]` done
- `[!]` blocked or needs attention

## Tasks

### Restore Desktop Downloader

Goal: return the copied Windows application to a stable, verifiable baseline before adding bots or clipping.

Planned changes:

- [x] Correct FFmpeg and non-FFmpeg format selectors.
- [x] Keep Tkinter access on the UI thread and pass plain snapshots to workers.
- [x] Invalidate qualities after URL or cookie-setting changes.
- [x] Disable browser cookies by default and stabilize the downloads path.
- [x] Update tests, documentation, runbook, stack inventory, and workflow contract.

Execution order:

- [x] Restore and verify the desktop application.
- [ ] Extract reusable media download and processing services.
- [ ] Add clipping, compression, and destination-platform profiles.
- [ ] Add the first social-network bot transport.

Risks or dependencies:

- [ ] Real downloads still depend on remote site behavior, yt-dlp updates, Node.js, and FFmpeg availability.
- [ ] The first bot platform and its delivery limits still need to be selected.

Verification:

- [x] Unit tests and Python compilation.
- [x] Hidden Tkinter construction and stale-state smoke test.
- [x] Live YouTube metadata lookup without downloading media.
