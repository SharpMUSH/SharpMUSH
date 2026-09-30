export function focusSection(article, id) {
    const section = article?.ownerDocument.getElementById(id);
    if (section && article.contains(section)) {
        section.scrollIntoView({ block: "start" });
    }
}
