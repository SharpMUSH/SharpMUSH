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
