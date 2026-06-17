// wiki-render.js — client-side post-render pass for wiki articles.
//
// Two jobs, both invoked from WikiDisplay.razor's OnAfterRenderAsync via JSInterop:
//   1. Mermaid: Markdig's Diagrams extension emits `<pre class="mermaid">…</pre>` for
//      ```mermaid fences; mermaid.js turns those into SVG. We initialise once with a
//      strict security profile (wiki pages are user-editable) and run it after each render.
//   2. Anchor scrolling: Blazor WASM has no built-in scroll-to-fragment, and its global
//      <a> click interceptor resolves bare `#frag` links against <base href="/">, sending
//      them to the site root. We intercept `a[href^="#"]` clicks in the capture phase
//      (before Blazor sees them), scroll to the target, and update the hash ourselves —
//      and scroll to the URL fragment on load / hashchange for deep links.
//
// Loaded as a classic script before blazor.webassembly.js (see index.html), so the global
// is present by the time OnAfterRenderAsync fires.
(function () {
    "use strict";

    window.SharpMUSH = window.SharpMUSH || {};

    let _initialised = false;

    function scrollToHash() {
        const raw = window.location.hash;
        if (!raw || raw.length < 2) return;
        let id;
        try { id = decodeURIComponent(raw.slice(1)); } catch { id = raw.slice(1); }
        const el = document.getElementById(id);
        if (el) el.scrollIntoView({ behavior: "smooth", block: "start" });
    }

    // Capture-phase handler: only acts on in-page fragment links inside wiki content,
    // so it never swallows real navigation links.
    function onDocumentClick(ev) {
        const anchor = ev.target && ev.target.closest ? ev.target.closest('a[href^="#"]') : null;
        if (!anchor) return;
        if (!anchor.closest(".WikiContent, .wiki-toc")) return;

        const href = anchor.getAttribute("href");
        if (!href || href.length < 2) return;
        let id;
        try { id = decodeURIComponent(href.slice(1)); } catch { id = href.slice(1); }
        const el = document.getElementById(id);
        if (!el) return;

        // Stop Blazor's interceptor from turning "#frag" into a root navigation.
        ev.preventDefault();
        ev.stopPropagation();
        el.scrollIntoView({ behavior: "smooth", block: "start" });
        if (window.history && window.history.replaceState) {
            window.history.replaceState(null, "", href);
        } else {
            window.location.hash = href;
        }
    }

    window.SharpMUSH.WikiRender = {
        // Idempotent one-time setup. Called on the wiki view's first render.
        init: function () {
            if (_initialised) return;
            _initialised = true;

            if (typeof window.mermaid !== "undefined") {
                try {
                    window.mermaid.initialize({
                        startOnLoad: false,
                        securityLevel: "strict", // no HTML labels / click handlers in diagrams
                        theme: "dark"            // matches the portal's dark surfaces
                    });
                } catch (e) {
                    console.warn("SharpMUSH.WikiRender: mermaid.initialize failed", e);
                }
            }

            // One global capture-phase listener handles every wiki page.
            document.addEventListener("click", onDocumentClick, true);
            window.addEventListener("hashchange", scrollToHash);
        },

        // Called after every article render: turn any new mermaid blocks into SVG, then
        // honour a deep-link fragment once the content (and its ids) exist in the DOM.
        render: async function () {
            if (typeof window.mermaid !== "undefined") {
                try {
                    // `:not([data-processed])` skips diagrams mermaid has already rendered.
                    await window.mermaid.run({
                        querySelector: ".WikiContent .mermaid:not([data-processed])"
                    });
                } catch (e) {
                    console.warn("SharpMUSH.WikiRender: mermaid.run failed", e);
                }
            }
            scrollToHash();
        }
    };
})();
