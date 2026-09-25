# uBlock Origin Lite

YouTube Viewer bundles uBlock Origin Lite by Raymond Hill and contributors as
a separate WebView2 browser extension, licensed under GPL-3.0-or-later.
The complete extension source (JavaScript, HTML, rules and resources) and its
`LICENSE.txt` are distributed in `Extensions/uBlockOriginLite/` beside the app.

- Project and source: https://github.com/uBlockOrigin/uBOL-home
- Release: https://github.com/uBlockOrigin/uBOL-home/releases/tag/2026.914.1325
- Build sources: https://github.com/gorhill/uBlock/tree/master/platform/mv3
- Archive URL, version and SHA256: `adblock.lock.json` in the Viewer sources.

Viewer packaging adds a public `key` to the extension manifest for stable
identity across install locations. This modification is made by
`build_support/restore-adblock.ps1`; no filtering code or rules are modified.
The public key is an identifier, not a signing credential.
Filter lists retain the notices and licenses provided by their authors.

The extension is bundled and runs locally. Updating its bundled rules requires
a new Viewer build with a reviewed, pinned release and matching checksum.
