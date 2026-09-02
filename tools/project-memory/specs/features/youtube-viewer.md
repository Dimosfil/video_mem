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
5. Before opening the window, the application verifies that the configured Happ
   HTTP proxy is reachable.
6. WebView2 receives `--proxy-server=http://127.0.0.1:10809 --disable-quic`, so
   browser traffic uses Happ instead of a direct route. The proxy URL can be
   overridden with `YOUTUBE_VIEWER_PROXY` when the local Happ port changes.
7. When the proxy is unavailable, the viewer reports the problem and exits
   without loading YouTube directly.
8. The user can create, close, switch, and restore real WebView2 tabs. Requests
   for a new browser window open in a new application tab.
9. Back, forward, reload, hard reload, home, stop, address navigation, YouTube
   search, and full-screen operations are available through buttons or standard
   browser keyboard shortcuts.
10. Web cookies and local state persist under the stable external root
   `%LOCALAPPDATA%\VideoMem\YouTubeViewer` between launches, rebuilds, and
   executable reinstalls. WebView2 owns the single `EBWebView` child directory;
   the application must not append that child name itself.
11. WebView file downloads and external-browser popups are disabled.

## Boundaries

- `youtube_viewer` does not import `yt-dlp`, downloader source, cookies.txt, or
  media format logic.
- The viewer does not download, process, rename, or save video files.
- The viewer owns its window, tab/navigation state, and persistent web profile.
- Build and publish operations must never delete the external WebView profile.
- Microsoft Edge WebView2 Runtime is the expected Windows rendering engine.
- .NET 8 Desktop Runtime is the expected application runtime.
- The default local proxy dependency is Happ HTTP on `127.0.0.1:10809`.
- The existing root `app.py` and `downloads/` workflow remain unchanged.
- The packaged process name is `YouTube Viewer`; the windowed build does not
  expose a Python console.
- Windows grouping uses AppUserModelID `Dimosfil.VideoMem.YouTubeViewer`.
- The executable embeds a dedicated application icon and version resource.
- Build and distribution outputs remain ignored and are not committed.

## Verification

- Unit-test address resolution, proxy configuration, QUIC blocking, and bounded
  closed-tab restoration, including the non-duplicated stable profile root.
- Build the WPF project with zero warnings and errors.
- Start the independent launcher and verify a live `YouTube Viewer` window.
- Use Windows UI Automation to verify the navigation controls are exposed and a
  second real tab can be created.
- Navigate to a second page and verify back/forward state updates.
- Inspect the live WebView2 process tree and verify all established viewer-tree
  TCP connections terminate at `127.0.0.1:10809`, with zero direct remote
  connections.
- Verify the live process name is `YouTube Viewer` and its executable path is
  under `youtube_viewer/dist/`, with no viewer process running from `.venv`.
- Read the executable version resource and confirm product name, description,
  and original filename.
- Run downloader regression tests to prove separation did not change the root
  application.
