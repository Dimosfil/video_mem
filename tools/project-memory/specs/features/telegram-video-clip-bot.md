# Telegram Video Clip Bot

## Goal

Let a Telegram user submit one YouTube video, enter start and end timestamps in the private chat, confirm the selection, and receive a portable streaming-compatible MP4 clip without a website or Mini App.

## Workflow Contract

1. The user sends `/start` and receives a concise usage instruction.
2. The user sends an HTTP(S) URL for one supported YouTube video.
3. The Telegram gateway rejects non-YouTube URLs, group messages, and unsupported updates.
4. The media adapter retrieves metadata without downloading the full video.
5. The application cancels any unfinished selection for the same user and creates a persistent job in `awaiting_start` state.
6. The bot sends the title and duration, then asks for a start timestamp in seconds, `MM:SS`, or `HH:MM:SS` form.
7. A valid start changes the job to `awaiting_end`; a valid end changes it to `confirming`.
8. The server requires `0 <= start < end <= duration` and enforces the configured maximum clip duration.
9. The bot shows `Получить видео` and `Отмена` callback buttons containing the job UUID.
10. A callback is accepted only when the Telegram chat and user own that job and its state is still `confirming`.
11. Confirmation changes the job to `queued`; a bounded worker changes it to `processing`.
12. yt-dlp downloads the requested time range only. FFmpeg then produces a 720-high H.264/AAC MP4 with `faststart` and a bitrate bounded for Telegram delivery.
13. The job changes to `sending`, and the Telegram adapter calls `sendVideo` with streaming enabled.
14. Successful delivery changes the job to `done`. Any processing or delivery failure changes it to `failed`, stores a safe user-facing error, and attempts to notify the chat.

## State Model

```text
awaiting_start -> awaiting_end -> confirming -> queued -> processing -> sending -> done
       |               |             |                      |           |
       +----------> cancelled <-------+                      +-> failed <-+
```

Completed jobs are immutable in the MVP. A user sends the URL again to create another clip.

## Security And Resource Boundaries

- Accept only allowlisted YouTube hosts and recognized single-video URL forms.
- Never accept the Telegram token through an HTTP route or store it in job records.
- Verify both Telegram chat ID and user ID before accepting confirmation or cancellation callbacks.
- Resolve all artifacts inside the configured job root; derive filesystem paths from validated UUID job IDs.
- Keep Telegram polling, worker count, clip duration, upload size, media timeout, TTL, cookie file, and FFmpeg path in environment-backed configuration.
- Keep real `.env`, cookies, job records, and artifacts ignored by Git.
- Do not require an HTTP server, public domain, HTTPS tunnel, browser session, or Mini App for the MVP.

## MVP Verification

- Unit-test supported and rejected URL forms and timestamp parsing.
- Unit-test range limits, active-job lookup, and Telegram ownership checks.
- Test the chat state transitions through confirmation without network calls.
- Verify callback acknowledgement, callback payloads, and `supports_streaming` delivery with fake Telegram HTTP boundaries.
- Run all existing desktop downloader tests.
- Perform a live metadata lookup and a short real range download; inspect the artifact with FFprobe for duration, H.264 video, AAC audio, and MP4 size.
- Real Telegram polling and delivery remain environment verification requiring only a bot token and Telegram network access.
