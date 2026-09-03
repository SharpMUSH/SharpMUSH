// On-demand loading for the portal's heavy editor assets.
//
// These used to be plain <script> tags in index.html, so every visitor of the home page fetched
// Monaco (3.67 MB) and Mermaid (2.57 MB) before they had done anything — whether or not they would
// ever open the softcode editor or a wiki page with a diagram in it. Compression cut what those cost
// on the wire; only this stops them being asked for.
//
// Each bundle loads at most once. ensure() hands every caller the SAME promise, so a component that
// asks twice (or two components asking at once) waits on one set of network requests rather than
// racing to inject duplicate tags.
(function () {
    "use strict";

    window.SharpMUSH = window.SharpMUSH || {};

    // Order within a bundle matters and is preserved: Monaco's AMD loader has to exist before
    // editor.main.js runs, and our own configuration after that.
    const bundles = {
        monaco: {
            css: [],
            js: [
                "_content/BlazorMonaco/jsInterop.js",
                "_content/BlazorMonaco/lib/monaco-editor/min/vs/loader.js",
                "_content/BlazorMonaco/lib/monaco-editor/min/vs/editor/editor.main.js",
                "js/mush-monaco.js"
            ],
            // editor.main.js bootstraps through the AMD loader, so the script's load event fires
            // before window.monaco exists. Without this wait a caller can render an editor against a
            // global that is not there yet — the exact ReferenceError the old eager tags avoided by
            // loading everything before Blazor started.
            ready: () => typeof window.monaco !== "undefined" && !!window.monaco.editor
        },
        mermaid: {
            css: [],
            js: ["js/mermaid.min.js"],
            ready: () => typeof window.mermaid !== "undefined"
        }
    };

    const inFlight = {};

    function injectScript(url) {
        return new Promise((resolve, reject) => {
            const existing = document.querySelector(`script[data-lazy-asset="${url}"]`);
            if (existing) { resolve(); return; }
            const el = document.createElement("script");
            el.src = url;
            el.async = false; // preserve execution order within a bundle
            el.dataset.lazyAsset = url;
            el.onload = () => resolve();
            el.onerror = () => reject(new Error(`failed to load ${url}`));
            document.head.appendChild(el);
        });
    }

    function injectCss(url) {
        return new Promise((resolve) => {
            const existing = document.querySelector(`link[data-lazy-asset="${url}"]`);
            if (existing) { resolve(); return; }
            const el = document.createElement("link");
            el.rel = "stylesheet";
            el.href = url;
            el.dataset.lazyAsset = url;
            // Styling is cosmetic: a stylesheet that 404s must not block the editor from opening.
            el.onload = () => resolve();
            el.onerror = () => resolve();
            document.head.appendChild(el);
        });
    }

    // Bounded wait for a bundle's global to appear. Bounded rather than open-ended so a failed or
    // blocked asset surfaces as a rejected promise the caller can render an error for, instead of a
    // page that waits forever with no explanation.
    async function waitFor(predicate, timeoutMs) {
        const deadline = Date.now() + timeoutMs;
        while (!predicate()) {
            if (Date.now() > deadline) throw new Error("asset did not initialise in time");
            await new Promise((r) => setTimeout(r, 25));
        }
    }

    window.SharpMUSH.Assets = {
        /// Loads a named bundle once. Returns a promise that settles when it is usable.
        ensure: function (name) {
            if (inFlight[name]) return inFlight[name];

            const bundle = bundles[name];
            if (!bundle) return Promise.reject(new Error(`unknown asset bundle '${name}'`));

            inFlight[name] = (async () => {
                await Promise.all(bundle.css.map(injectCss));
                for (const js of bundle.js) {
                    await injectScript(js);
                }
                if (bundle.ready) await waitFor(bundle.ready, 30000);
            })();

            // A failed load must not be cached as permanently broken: a reader who lost the network
            // for one request should get a real attempt the next time they open the page.
            inFlight[name].catch(() => { delete inFlight[name]; });

            return inFlight[name];
        },

        /// Whether a bundle is already usable, without triggering a load.
        isLoaded: function (name) {
            const bundle = bundles[name];
            return !!bundle && (!bundle.ready || bundle.ready());
        }
    };
})();
