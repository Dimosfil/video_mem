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

- Drag a tab header with the left mouse button past the Windows drag threshold;
  drop on the left/right half of another header to insert before/after it.
  Dropping outside the tab headers or pressing Escape cancels the operation.
- Reordering retains the existing tab and WebView2 instances, navigation state,
  and selected tab. The internal order and displayed order remain identical.
  The close button is not a drag handle; page drag/drop is not a tab move.
- A header context menu targets the clicked tab, including background tabs.
  It offers current, others, left, right, all, and reopen-last-closed commands.
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
  restore selection before initializing WebViews asynchronously. Suppress writes
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
