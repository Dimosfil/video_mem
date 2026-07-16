# YouTube Downloader Feature

## Goal

Provide a minimal Windows desktop workflow for downloading a YouTube video after the user pastes a URL, chooses an available quality, and starts the download.

## Workflow Contract

1. The user opens the desktop app.
2. The user pastes an `http://` or `https://` YouTube URL.
3. The user requests available qualities.
4. The app asks `yt-dlp` for metadata without downloading the video.
5. If the user selects a browser in the Cookies dropdown, the app passes that browser to `yt-dlp` for local cookie loading.
6. If browser cookie decryption fails, the user can select `Файл cookies.txt` and choose a Netscape-format cookies file instead.
7. The quality dropdown is populated from discovered formats.
8. The user chooses a destination folder and quality.
9. The same cookie setting is reused for the download.
10. The app downloads the video in the background and reports progress in the window.
11. Terminal states are success with a saved file path, metadata lookup failure, validation failure, or download failure.
12. Changing the URL, cookie browser, browser profile, or cookie file invalidates discovered qualities and requires a new metadata request.

## Format Rules

- With FFmpeg available, quality options include video-only formats because `yt-dlp` can merge best audio and video streams.
- Without FFmpeg, quality options include only progressive formats with both audio and video in one file.
- Selectors used without FFmpeg never request separate video and audio streams.
- The first quality option is always the best available selector for the current runtime capability.
- Playlist downloading is disabled; each action targets one URL.
- Format selectors prefer Windows-friendly MP4 output: H.264/AVC video (`avc1`) and M4A/AAC audio, avoiding AV1/Opus when possible.
- Browser cookie loading uses `yt-dlp`'s `cookiesfrombrowser` option with supported browsers exposed in the UI.
- Browser cookie loading can include an optional profile name such as `Default` or `Profile 1`.
- Cookie-file loading uses `yt-dlp`'s `cookiefile` option and is the fallback for DPAPI decryption failures.
- The cookie-file picker is always available; choosing a file switches the app to `Файл cookies.txt` mode.
- When Node.js is available, the app passes it to yt-dlp as the JavaScript runtime for YouTube challenge solving.
- Downloaded filenames include the selected quality label, e.g. `[1080p]` or `[best]`.
- Browser cookies are disabled by default and are read only after explicit user selection.
- The default destination is the project-local `downloads/` directory, independent of the shell working directory.

## Concurrency Rules

- Tkinter variables and widgets are accessed only from the main UI thread.
- URL and cookie settings are converted to a plain yt-dlp options snapshot before a worker starts.
- Workers publish progress, terminal results, and discovered formats through the UI message queue.
- A metadata result is discarded when its URL or settings revision no longer matches the current UI request.

## Verification

- Unit-test `build_quality_options` for FFmpeg and non-FFmpeg format selection.
- Unit-test compatible format filtering.
- Unit-test browser-cookie and cookie-file option mapping.
- Unit-test JavaScript runtime option shape.
- Unit-test filename quality-label mapping.
- Unit-test that non-FFmpeg selectors contain no merge operation.
- Unit-test yt-dlp option snapshots for no-cookie, browser-cookie, cookie-file, and missing-file cases.
- Compile/import the application with `yt-dlp` installed.
- Avoid committing downloaded media, local virtual environments, or generated caches.
