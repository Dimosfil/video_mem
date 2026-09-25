# Connected Projects

## uBlock Origin Lite

- Source: https://github.com/uBlockOrigin/uBOL-home (upstream extension by Raymond Hill).
- Role: separate, bundled Manifest V3 content-blocking extension in the Viewer WebView2 profile.
- Retrieval: `youtube_viewer/adblock.lock.json` pins release URL, version and SHA256.
  `build_support/restore-adblock.ps1` restores it into ignored `build/adblock/`;
  MSBuild copies complete extension source/rules/license to `Extensions/uBlockOriginLite/`.
- Modification: stable public manifest key for identity across install paths;
  filtering engine and rules are upstream. See `youtube_viewer/THIRD-PARTY-NOTICES.md`.
- Updates: review/pin another release and checksum, test WebView2 filtering, ship
  a new installer. Do not download or replace extensions during normal startup.
- Boundary: only the Viewer profile; no system browser, VPN, DNS, proxy or OS
  routing changes. Broad host permission is used by upstream for optimal mode.

## telegram_bot_template

- Purpose: reusable Telegram-first backend template and reference implementation.
- Local folder: `D:\AI\telegram_bot_template`.
- Source of truth: the local Git repository at that folder.
- Role in this project: architecture and implementation reference for environment-backed bot configuration, long polling, Telegram API payloads, client-gateway separation, and test boundaries.
- Adopted scope: only the patterns required by the video clipping MVP; unrelated admin, guide delivery, analytics, AI router, and TypeScript monorepo modules were not copied.
- Runtime dependency: none. `video_mem` does not import or execute files from the template after implementation.
- Update procedure: explicitly inspect the template again only when changing the Telegram gateway contract or adopting another template feature.
- Access boundary: read only when the user explicitly authorizes work involving this external project; never read its `.env`, runtime databases, logs, uploads, or private deployment data for normal `video_mem` work.
