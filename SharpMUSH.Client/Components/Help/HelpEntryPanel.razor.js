const sectionHandlers = new WeakMap();

export function bindSections(article) {
    if (!article || sectionHandlers.has(article)) return;
    const handler = event => {
        if (event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        const link = event.target.closest?.("nav.help-toc a[href^='#']");
        if (!link || !article.contains(link)) return;
        const fragment = link.getAttribute("href");
        const id = decodeURIComponent(fragment.slice(1));
        const section = article.ownerDocument.getElementById(id);
        if (!section || !article.contains(section)) return;
        event.preventDefault();
        const view = article.ownerDocument.defaultView;
        const destination = new URL(view.location.href);
        destination.hash = fragment;
        view.history.pushState(view.history.state, "", destination);
        focusSection(article, id);
    };
    article.addEventListener("click", handler);
    sectionHandlers.set(article, handler);
}

export function unbindSections(article) {
    const handler = sectionHandlers.get(article);
    if (handler) article.removeEventListener("click", handler);
    sectionHandlers.delete(article);
}

export function focusSection(article, id) {
    const section = article?.ownerDocument.getElementById(id);
    if (section && article.contains(section)) {
        section.scrollIntoView({ block: "start" });
        if (!section.hasAttribute("tabindex")) {
            section.setAttribute("tabindex", "-1");
            section.addEventListener("blur", () => section.removeAttribute("tabindex"), { once: true });
        }
        section.focus({ preventScroll: true });
    }
}
