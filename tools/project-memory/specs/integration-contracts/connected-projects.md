# Connected Projects

## telegram_bot_template

- Purpose: reusable Telegram-first backend template and reference implementation.
- Local folder: `D:\AI\telegram_bot_template`.
- Source of truth: the local Git repository at that folder.
- Role in this project: architecture and implementation reference for environment-backed bot configuration, long polling, Telegram API payloads, client-gateway separation, and test boundaries.
- Adopted scope: only the patterns required by the video clipping MVP; unrelated admin, guide delivery, analytics, AI router, and TypeScript monorepo modules were not copied.
- Runtime dependency: none. `video_mem` does not import or execute files from the template after implementation.
- Update procedure: explicitly inspect the template again only when changing the Telegram gateway contract or adopting another template feature.
- Access boundary: read only when the user explicitly authorizes work involving this external project; never read its `.env`, runtime databases, logs, uploads, or private deployment data for normal `video_mem` work.
