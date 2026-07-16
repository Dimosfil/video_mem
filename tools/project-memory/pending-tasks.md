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
- [ ] Add provider-neutral audio transcription with text, timestamped JSON, and SRT artifacts.
- [ ] Expose the reusable media and transcription workflow to LLM clients through a standard MCP adapter.
- [~] Add clipping, compression, and destination-platform profiles (Telegram MVP implemented; additional profiles remain).
- [x] Add the first social-network bot transport (Telegram).

Risks or dependencies:

- [ ] Real downloads still depend on remote site behavior, yt-dlp updates, Node.js, and FFmpeg availability.
- [ ] The first transcription engine must be selected: local Whisper or a configurable cloud provider.
- [ ] MCP must remain a transport adapter; downloading, transcription, and artifact storage must not depend on MCP or Tkinter.
- [ ] API keys must stay in environment or secret storage and must not be accepted as ordinary LLM tool arguments or written to logs.
- [x] Select Telegram as the first bot platform and enforce its configured delivery limit.

Verification:

- [x] Unit tests and Python compilation.
- [x] Hidden Tkinter construction and stale-state smoke test.
- [x] Live YouTube metadata lookup without downloading media.

### Transcription And LLM Access

Goal: turn downloaded media into reusable text artifacts and allow an LLM client to invoke the same workflow without coupling product logic to a specific UI or protocol.

Planned workflow:

- [ ] Accept a downloaded media artifact and extract or reuse its audio track.
- [ ] Transcribe speech through a provider-neutral adapter.
- [ ] Produce plain text, timestamped JSON, and SRT outputs; add speaker labels only when supported by the selected provider.
- [ ] Keep transcription callable from the desktop application before introducing a remote transport.
- [ ] Define stable operations for media inspection, audio download, transcription, artifact lookup, and status reporting.
- [ ] Add a standards-compliant MCP adapter over those operations for LLM clients.
- [ ] Add higher-level LLM workflows such as summary, structured notes, quote extraction, and subtitle preparation after the base transcript contract is verified.

Release criteria:

- [ ] A user can transcribe one downloaded video and obtain text plus timestamps without using MCP.
- [ ] The same transcription operation can be invoked through MCP without duplicating download or transcription logic.
- [ ] Long-running work has progress, cancellation, timeouts, bounded concurrency, and actionable failures.
- [ ] Cached artifacts have explicit identity, retention, size limits, and safe access boundaries.
- [ ] Automated tests cover the workflow contract, provider failures, cache behavior, and MCP request validation.

### Telegram Video Clip MVP

Goal: let a Telegram user send one YouTube link, enter a start and end time in the private chat, confirm the range, and receive a streaming-compatible MP4 clip in the same chat without a website.

Implementation plan:

- [x] Reuse the Telegram gateway and configuration patterns from `D:\AI\telegram_bot_template` without importing unrelated admin, guide-bot, or AI modules.
- [x] Add a Telegram polling gateway that accepts private-chat YouTube links and callback queries.
- [x] Add a persistent job repository with ownership data, statuses, errors, and artifact paths.
- [x] Add a chat dialogue that asks for start and end timestamps, validates the range, and shows confirm/cancel buttons.
- [x] Add a bounded media worker that downloads only the selected section through yt-dlp and normalizes it through FFmpeg.
- [x] Deliver the result with Telegram `sendVideo`, streaming enabled, and a configured upload-size boundary.
- [x] Add runtime cleanup, URL allowlisting, maximum clip duration, timeouts, safe errors, and configuration through environment variables.
- [x] Document local startup without a site, domain, or public URL.
- [x] Add a Docker/Compose runtime with FFmpeg, Node.js, restart policy, ignored secrets, and persistent job data without published ports.

MVP release criteria:

- [x] `/start` explains the workflow and a YouTube URL starts the timestamp dialogue.
- [x] The chat dialogue rejects invalid or oversized ranges before processing.
- [x] One valid selection produces an H.264/AAC MP4 artifact and advances the job to `done`.
- [x] A completed artifact is handed to the Telegram delivery adapter with `supports_streaming=true`.
- [x] Tests cover URL validation, timestamp/range validation, job ownership, dialogue transitions, callbacks, and Telegram payloads without a real bot token.

Remaining deployment verification:

- [x] Docker image and Compose configuration build successfully; Python, FFmpeg, Node.js, `libx264`, the data mount, and the bot entrypoint pass credential-free smoke checks.
- [!] Real Telegram polling and video delivery require a bot token and network access and were not executed in the local credential-free check.

Later option:

- [ ] Consider an optional Telegram Mini App visual editor after the chat-only MVP is validated with real users.
