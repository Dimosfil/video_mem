# YouTube Viewer

## Goal

Provide a separate Windows desktop application under `youtube_viewer/` for
watching YouTube in a dedicated native window. The existing root application
continues to own video downloading; the viewer must not expose or depend on the
download workflow.

## Workflow Contract

1. The user launches `.\youtube_viewer\start.ps1`.
2. When the executable is missing, the launcher runs the reproducible local
   locked NuGet restore and .NET publish build. It also rebuilds when viewer
   source is newer than the executable.
3. The launcher starts `youtube_viewer/dist/YouTube Viewer/YouTube Viewer.exe`
   directly; normal use must not run the viewer through `python.exe`.
4. The application opens `https://www.youtube.com/` in a resizable desktop
   WPF window backed by Microsoft Edge WebView2.
5. The default route is the Windows system connection, which also covers a
   system-wide VPN and does not depend on a named VPN client.
6. The toolbar settings dialog can select an arbitrary local HTTP proxy for
   application-specific routing. In that mode WebView2 receives
   `--proxy-server=<configured URL> --disable-quic`.
7. When the selected local proxy is unavailable, the viewer offers connection
   settings and does not load YouTube directly until the route is changed or
   the endpoint becomes available.
8. The user can create, close, switch, and restore real WebView2 tabs. Requests
   for a new browser window open in a new application tab.
   Window closure and relaunch restore all open URLs, their order, and the
   selected tab from the stable profile's `session.json`.
9. Back, forward, reload, hard reload, home, stop, address navigation, YouTube
   search, and full-screen operations are available through buttons or standard
   browser keyboard shortcuts.
10. Web cookies, local state, and `settings.json` persist under the stable external root
   `%LOCALAPPDATA%\VideoMem\YouTubeViewer` between launches, rebuilds, and
   executable reinstalls. WebView2 owns the single `EBWebView` child directory;
   the application must not append that child name itself.
11. WebView file downloads and external-browser popups are disabled.
12. `packaging/windows/build.ps1` resolves the application version from the
    project, creates a self-contained win-x64 payload, and compiles a per-user
    Inno Setup installer with the same version in its metadata and filename.
13. Installing, upgrading, or uninstalling application binaries must not remove
    `%LOCALAPPDATA%\VideoMem\YouTubeViewer`; login state and connection settings
    remain available to the next installed version.

## Tab Management Contract

- The tab strip ends with a real "+" button using the existing Ctrl+T/home-tab
  creation path. Headers scroll horizontally when space runs out; the button
  stays outside the scrolling region and is never counted as a browser tab.
- "Duplicate tab" targets the context-menu tab, including a background or
  uninitialized tab, and opens its last known/requested URL immediately to its
  right. Select and persist the independent new WebView; retain the original.
  Navigation history and exact playback position are not copied.
- Verified on 2026-09-14: 21/21 logic tests and isolated live WPF/WebView2
  checks pass, including the tab-strip button at minimum width, background
  duplication of a changed URL, independent document state and session order.
  Runtime evidence: `downloads/viewer-runtime-test-99d816d78bbb45a4bcda34c8a8a3d265/`.
- Drag a tab header with the left mouse button past the Windows drag threshold;
  drop on the left/right half of another header to insert before/after it.
  Dropping outside the tab headers or pressing Escape cancels the operation.
- Reordering retains the existing tab and WebView2 instances, navigation state,
  and selected tab. The internal order and displayed order remain identical.
  The close button is not a drag handle; page drag/drop is not a tab move.
- A header context menu targets the clicked tab, including background tabs.
  It offers duplicate, current, others, left, right, all, and reopen-last-closed commands.
  Left/right use the current displayed order and exclude the clicked tab.
  Empty groups and empty restoration history disable their menu commands.
- Bulk closure snapshots its target group before removal. If the active tab is
  in that group and the anchor survives, select the anchor before closing.
  Each closed WebView is disposed through the ordinary close path; addresses
  enter the existing bounded history in left-to-right closure order, so
  Ctrl+Shift+T restores them individually in reverse order.
- Closing the last tab, including through Close All, closes the window as before.
  Closed history is in-memory and is not restored after the application exits.
- Closing an initializing tab must not navigate or configure a disposed WebView;
  its requested address is available for restoration before initialization.
- Regression checks cover target selection after reorder, first/last/single-tab
  boundaries, snapshot removal/history, and insertion in both directions.
- `youtube_viewer/tests/ui/YouTubeViewer.UiTests.csproj` exercises real WPF menu
  resources and routed commands, tab/WebView identity, selection, model/display
  order, and last-tab window closure without opening a window or user profile.
  Verified on 2026-09-05 together with all 17 logic tests and Release publish.
  Physical mouse dragging and continuity of live video playback remain manual
  interaction checks; the headless WPF test does not exercise the WebView engine.

## Session Persistence Contract

- Persist ordered open HTTP(S) addresses and the selected index under
  `ViewerProfile.UserDataFolder()/session.json`, outside build/install folders.
  Save on tab creation, selection, movement, closure, navigation/SPA source
  changes, and before window disposal. Do not query browser history or cookies
  to infer missing tabs.
- A window close, OS/application shutdown, or connection-settings restart saves
  the open set. Explicitly closing a tab removes it from the saved set; Close All
  persists an empty set, so the next launch opens the home page.
- Read the saved set at startup; build every tab placeholder synchronously and
  restore selection before initializing only the selected WebView asynchronously.
  Initialize each other WebView on first selection and retain its initialization
  task to avoid duplicate handlers or navigation. Never await all hidden tabs:
  WPF WebView2 initialization may wait until a tab becomes visible. Suppress writes
  until all placeholders exist, and ignore late callbacks from disposed tabs.
  Closing during initialization must retain every requested URL.
- Write by atomic replacement with a backup of the previous readable session.
  On malformed/unreadable primary data, read the backup. Skip non-HTTP(S)
  addresses and remap selection; preserve URL duplicates and order. Missing or
  empty sessions fall back to one home tab. Surface save I/O failures in status.
- Use one named process mutex for session-aware installed and project builds;
  acquire it before reading the profile, and hold it until application exit.
  Repeated launch activates the existing window without rewriting its session.
  Older binaries without this guard cannot participate in the protection.
- Historical versions never wrote open-tab state; no automatic recovery of
  already lost tabs is claimed. Persistence restores URLs, not per-tab browser
  navigation stacks or exact playback positions. Closed-tab history remains
  bounded and in-memory.
- Verification: logic tests round-trip Unicode/duplicate URLs and selection,
  check malformed-file backup recovery and intentional empty sessions; WPF tests
  create and close successive windows with an isolated session file and verify
  reordered tabs, navigation changes, selection and explicit bulk closure.
  A mutex ownership test checks exclusion and reacquisition after exit.
  Verified on 2026-09-05: 21 logic tests, the isolated WPF window-session
  regression, and Release publish to `youtube_viewer/dist/YouTube Viewer session-update/`.
  The running pre-persistence build was left open to avoid losing its unsaved
  tabs; no recovery of its old tab list or live-profile restart is claimed.

## Loading And Failure Contract

- Startup finishes when the selected restored tab is initialized; background
  placeholders keep their URLs and restore their saved document titles before loading.
  Older sessions without titles show the host until the document title arrives.
  The full requested address is available in the tab tooltip.
- Keep loading, navigation ID, failure and timeout state per tab. Selecting a
  completed tab clears the previous tab's loading indicator; late completion
  from an older navigation must not change the newer navigation's state.
- A navigation error or 30-second document-readiness deadline shows a WPF retry/settings
  panel. Hide the failed tab's native view so it cannot cover the panel. Retain
  the real requested URL in the session. Do not change proxy/system routing.
- Cancel the deadline at top-level DOMContentLoaded for the matching navigation
  ID and finish the loading indicator. NavigationCompleted waits for auxiliary
  resources too: slow images must not stop/hide a usable document or player.
  Ignore readiness events from superseded or closed tabs and failed navigations.
- A retry clears the error, shows the native view and navigates to the saved
  requested URL. Initialization failures remain local to that tab and can retry;
  they must not close the window or discard other restored tabs.
- Cancel navigation watchdogs when the navigation completes, another starts,
  or the tab/window closes. A background failure must not cover the active page.
- The `--webview` UI-test mode uses a plain test Application (no production
  startup), an ignored isolated profile, an off-screen non-activating window,
  and intercepted HTTP responses. It exercises seven restored real WebViews,
  viewport/document switching, JavaScript state after reorder, a real navigation
  deadline, background failure isolation and successful retry.
- Include a ready HTML document with an indefinitely delayed image alongside a
  delayed top-level document. After the real deadline, only the latter fails;
  the former retains its navigation/document. `--playback-url` accepts a public
  YouTube watch URL and verifies advancing time and decoded frames beyond 35
  seconds in an isolated muted profile, excluding advertisements.
- On 2026-09-09 the pre-fix runtime test reproduced indefinite startup waiting:
  the selected tab initialized and loaded, while all six hidden initialization
  tasks remained pending. This proves a startup bug, not the exact network cause
  of the user's black YouTube page. Live YouTube/VPN connectivity is not covered
  by deterministic local-response tests.
- The corrected live test passed on 2026-09-09, including all seven loaded
  documents, visible viewports, retained JavaScript state after moving a tab,
  the actual 30-second timeout, background isolation and retry. Passing-run
  evidence directory: `downloads/viewer-runtime-test-11412c2aab51413c89cca9bb3ecec514/`.
- The separate `--youtube` smoke passed with a clean isolated profile and system
  routing on 2026-09-09: the user's public search URL rendered the YouTube search
  component and the normal first-visit consent dialog. Preview:
  `downloads/viewer-runtime-test-5ecb1b33f0454f0fa78e502f1aa87617/youtube-search.png`.
  This does not inspect the user's existing authenticated profile.

## Boundaries

- `youtube_viewer` does not import `yt-dlp`, downloader source, cookies.txt, or
  media format logic.
- The viewer does not download, process, rename, or save video files.
- The viewer owns its window, tab/navigation state, and persistent web profile.
- Build and publish operations must never delete the external WebView profile.
- Microsoft Edge WebView2 Runtime is the expected Windows rendering engine.
- .NET 8 Desktop Runtime is the expected application runtime.
- System routing is the product default. The optional proxy URL defaults to
  `http://127.0.0.1:10809` but is not tied to a particular VPN product.
- The existing root `app.py` and `downloads/` workflow remain unchanged.
- The packaged process name is `YouTube Viewer`; the windowed build does not
  expose a Python console.
- Windows grouping uses AppUserModelID `Dimosfil.VideoMem.YouTubeViewer`.
- The executable embeds a dedicated application icon and version resource.
- Build and distribution outputs remain ignored and are not committed.
- The installer uses a stable application identifier so a newer build upgrades
  the existing per-user installation instead of creating a second product.
- The default install location is `%LOCALAPPDATA%\Programs\YouTube Viewer` and
  installation does not require elevation.

## Verification

### Built-in ad blocking (1.3.5, 2026-09-16)

- Goal: block YouTube ads inside the Viewer without changing VPN, connection
  settings, proxy arguments, DNS or other browser profiles.
- Enable WebView2 extension support in the existing environment. Restore the
  checksum-pinned uBO Lite Edge release during MSBuild; bundle all extension
  source, rules and license. See `adblock.lock.json` and third-party notices.
- `AdBlocker` installs once per window/profile before user navigation. A stable
  public manifest key preserves its identity across build/install locations.
  Read the existing enabled flag before reinstalling, defaulting to enabled on
  first use. Never remove unrelated profile extensions.
- Startup/re-enable waits for the upstream worker, optimal filtering, host
  permission and content-script registration in a separate invisible controller.
  Close that controller after the readiness check; never put its dashboard URL
  into user history or persisted sessions. Readiness is bounded to 15 seconds
  plus individual script-call timeouts.
- Toolbar button reports on/off/unavailable. Toggle the profile extension and
  reload only the current page. Existing other pages can need a manual reload
  to remove already injected cosmetic filters. On initialization/readiness
  failure continue browsing, visibly report unavailable, and do not claim active
  protection. Normal startup does not download or silently update filter code.
- Verified: 21/21 logic tests; complete real WebView2 suite with long-feed
  thumbnail recovery, tabs, navigation deadlines and the actual uBO Lite worker.
  Advertising image requests fail with `ERR_BLOCKED_BY_CLIENT`; ordinary
  thumbnails are allowed; realistic YouTube ad-card markup is hidden while
  content/player remains. Disable/re-enable, persisted disabled state on
  reinstall and no duplicate blocker identity also pass.
  Evidence: `downloads/adblock-logic-tests.log`, `downloads/adblock-webview-tests.log`,
  `downloads/adblock-full-webview-tests.log`.
- Release: installer 1.3.5 built; installer/payload numeric versions both
  1.3.5.0, bundled extension version/key/license checked. SHA256 and size are in
  `downloads/youtube-viewer-installer-1.3.5.json`; build log is adjacent.
  Installation into the user's existing app was not performed.
- Remaining live verification gap: isolated public YouTube playback timed out
  before document readiness after 30 seconds over system routing. See
  `downloads/adblock-live-playback.log`. This does not prove real video ads are
  absent or diagnose the user's route. Do not change VPN/routing to work around
  it; the user explicitly excluded VPN changes from this task.

### Thumbnail recovery contract (1.3.4)

- 2026-09-15: 21/21 logic tests and the real WebView2 regression passed.
  The isolated fixture scrolls 150 cards, then checks dynamically appended and
  recycled cards, transient errors, a stalled response, the two-retry limit,
  offscreen deferral, unchanged successful images and retained scroll position.
  Evidence: `downloads/thumbnail-recovery-tests.log`. The user's authenticated
  YouTube session was not inspected; exact incident reproduction remains open.
- Installer 1.3.4 and payload Windows version parts match (1.3.4.0).
  SHA256, size and artifact path: `downloads/youtube-viewer-installer-1.3.4.json`;
  build log: `downloads/youtube-viewer-installer-1.3.4-build.log`.
  Installation and upgrade of the user's running copy were not performed.

- Register `ThumbnailRecovery.Script` before each tab's first navigation. Run
  only in the top-level YouTube document, including dynamically appended cards.
- Observe `yt-img-shadow img` and `yt-image img`. Only HTTPS `ytimg.com`
  `/vi/` and `/vi_webp/` images with a plain `src` are eligible; leave responsive
  `srcset`/`picture` selection and other sites/resources to the browser.
- Retry visible failed images after 2/4 seconds, and visible stalled images
  after 20 seconds. Allow at most two retries per image/source, four starts per
  second. Successful images, offscreen cards and hidden documents are skipped.
- Reassign the same source through WebView2 without query rewriting, profile
  clearing, page reload, route changes or losing scroll/player state. Reset
  state when a card is reused with a different URL. Unobserve removed cards
  and keep per-image state weakly referenced so long feeds do not retain them.
- The screenshot alone does not establish the cause in the user's session.
  Recovery handles transient image failures; it cannot repair a persistent
  network outage or guarantee compatibility with future YouTube markup.

- 2026-09-14: built Windows installer 1.3.3 with the tab-strip plus button and
  context-menu duplication. Installer and payload versions match; SHA256 and
  artifact size are recorded in `downloads/youtube-viewer-installer-1.3.3.json`.
  Build log: `downloads/youtube-viewer-installer-1.3.3-build.log`. Installation
  was not run. `AGENTS.md` now requires installer delivery after viewer changes.

- 2026-09-11: reproduced the false timeout with a ready document and a stalled
  image, then passed the real 30-second WebView2 regression after cancelling the
  watchdog at DOM readiness. 21/21 logic tests passed. Installer 1.3.2 built and
  installed with user approval; the original video tab title returned after a
  graceful restart. Build/install logs: `downloads/youtube-viewer-installer-1.3.2-build.log`
  and `downloads/youtube-viewer-install-1.3.2.log`.
  Playback remains unverified/blocked: isolated real-video smoke observed zero
  frames/time, then YouTube request timeouts in system, no-QUIC and optional
  local-proxy modes. Ordinary HTTPS succeeds both directly and through the proxy.
  Do not claim the timeout fix restores video delivery or alter routing silently.

- Installer 1.3.1 was built for the selected-tab startup and navigation-failure
  fixes on 2026-09-09. The 21 logic tests, WPF suite, live WebView2 regression
  and isolated real YouTube smoke passed. Build log:
  `downloads/youtube-viewer-installer-1.3.1-build.log`; artifact/hash manifest:
  `downloads/youtube-viewer-installer-1.3.1.json`. Installation was not run.

- Windows installer 1.3.0 was compiled on 2026-09-05 with tab reordering,
  bulk closure and persistent open-tab sessions. Artifact:
  `youtube_viewer/dist/installer/YouTube-Viewer-Setup-1.3.0.exe`.
  Inno compilation, installer/application version agreement and self-contained
  Windows Desktop/WebView2 payload checks passed. Build log and hash manifest:
  `downloads/youtube-viewer-installer-build.log` and
  `downloads/youtube-viewer-installer-1.3.0.json`.
  This packaging run did not execute install/uninstall or restart the user's
  running viewer; those runtime checks remain unverified for this artifact.

- Unit-test address resolution, system/local routing, settings persistence,
  proxy configuration, QUIC blocking, and bounded closed-tab restoration,
  including the non-duplicated stable profile root.
- Build the WPF project with zero warnings and errors.
- Start the independent launcher and verify a live `YouTube Viewer` window.
- Use Windows UI Automation to verify the navigation controls are exposed and a
  second real tab can be created.
- Navigate to a second page and verify back/forward state updates.
- In system mode, inspect the live WebView2 process tree and verify no forced
  proxy argument is present. In local-proxy mode, verify the configured
  `--proxy-server` and `--disable-quic` arguments and no direct remote viewer
  connections.
- Verify the live process name is `YouTube Viewer` and its executable path is
  under `youtube_viewer/dist/`, with no viewer process running from `.venv`.
- Read the executable version resource and confirm product name, description,
  and original filename.
- Run downloader regression tests to prove separation did not change the root
  application.
- Compile the Windows installer, verify its version/hash, perform a silent
  install into an isolated test directory, start the installed executable, and
  silently uninstall it without touching the external viewer profile.
