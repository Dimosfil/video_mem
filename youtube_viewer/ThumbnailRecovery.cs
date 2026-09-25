namespace YouTubeViewer;

internal static class ThumbnailRecovery
{
    // Runs in each new document, including YouTube's subsequent SPA feed updates.
    internal const string Script = """
        (() => {
            if (window !== window.top || !/(^|\.)youtube\.com$/.test(location.hostname)) return;
            const states = new WeakMap();
            const visible = new Set();
            const selector = 'yt-img-shadow img, yt-image img';
            const maxRetries = 2;
            const stalledAfter = 20000;
            function source(img) {
                if (img.srcset || img.closest('picture')) return null;
                const raw = img.getAttribute('src');
                if (!raw) return null;
                try {
                    const url = new URL(raw, location.href);
                    return url.protocol === 'https:' && /(^|\.)ytimg\.com$/.test(url.hostname)
                        && /^\/(vi|vi_webp)\//.test(url.pathname) ? raw : null;
                } catch { return null; }
            }
            function stateFor(img, now) {
                const url = source(img);
                if (!url) return null;
                let state = states.get(img);
                if (!state || state.url !== url) {
                    state = { url, retries: 0, since: now, failed: false, retrying: false };
                    states.set(img, state);
                }
                return state;
            }
            const observer = new IntersectionObserver(entries => {
                for (const entry of entries) {
                    if (entry.isIntersecting) {
                        visible.add(entry.target);
                        const state = stateFor(entry.target, performance.now());
                        if (state) state.since = performance.now();
                    } else visible.delete(entry.target);
                }
            });
            function track(root) {
                if (!(root instanceof Element)) return;
                if (root.matches(selector)) observer.observe(root);
                for (const img of root.querySelectorAll(selector)) observer.observe(img);
            }
            function untrack(root) {
                if (!(root instanceof Element)) return;
                const images = root.matches(selector) ? [root] : [];
                images.push(...root.querySelectorAll(selector));
                for (const img of images) {
                    observer.unobserve(img);
                    visible.delete(img);
                }
            }
            document.addEventListener('error', event => {
                const img = event.target;
                if (!(img instanceof HTMLImageElement) || !img.matches(selector)) return;
                const state = stateFor(img, performance.now());
                if (state) state.failed = true;
            }, true);
            function start() {
                track(document.documentElement);
                new MutationObserver(records => {
                    for (const record of records) {
                        for (const node of record.removedNodes) untrack(node);
                        for (const node of record.addedNodes) track(node);
                    }
                }).observe(document.documentElement, { childList: true, subtree: true });
                setInterval(() => {
                    if (document.hidden) return;
                    const now = performance.now();
                    let budget = 4;
                    for (const img of visible) {
                        if (!img.isConnected) { visible.delete(img); observer.unobserve(img); continue; }
                        const state = stateFor(img, now);
                        if (!state || state.retrying || state.retries >= maxRetries ||
                            (img.complete && img.naturalWidth > 0)) continue;
                        const failed = state.failed || (img.complete && img.naturalWidth === 0);
                        const delay = failed ? 2000 * (state.retries + 1) : stalledAfter;
                        if (now - state.since < delay || budget-- <= 0) continue;
                        state.retries++;
                        state.since = now;
                        state.failed = false;
                        state.retrying = true;
                        // Re-request the exact URL through the existing browser route.
                        // Do not rewrite signatures, clear the profile or reload the page.
                        img.removeAttribute('src');
                        setTimeout(() => {
                            if (img.isConnected && !img.hasAttribute('src') && !img.srcset)
                                img.setAttribute('src', state.url);
                            state.retrying = false;
                        }, 50);
                    }
                }, 1000);
            }
            if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true });
            else start();
        })();
        """;
}
