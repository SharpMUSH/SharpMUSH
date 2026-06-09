# Area 5: Wiki / Shared Content — TODO

## Pre-Implementation
- [ ] Review & confirm decisions (5.1–5.5) with project owner
- [ ] Identify any decisions that need revision based on current codebase state

## Implementation Tasks
- [ ] Define wiki page schema (collection: title, namespace, markdown, rendered_html, text_plain, metadata)
- [ ] Implement Markdig pipeline (extensions, DisableHtml, wiki-link resolver)
- [ ] Implement wiki-link extension (`[[Page Name]]` → resolved links, redlink CSS)
- [ ] Wiki CRUD: create, read, update (with revision history)
- [ ] Revision history storage (diffs or full snapshots — decide at impl time)
- [ ] Page protection/locking (Royalty+ can protect pages)
- [ ] @wiki in-game commands: `@wiki/view`, `@wiki/edit`, `@wiki/search`
- [ ] Markdown → MString custom renderer (for in-game wiki display)
- [ ] HTTP handler: serve wiki pages for portal
- [ ] NATS event on wiki edit (`portal.wiki.changes`)
- [ ] Rendered HTML cache (invalidate on edit)
- [ ] Plain text extraction for search index (on write)

## Web UI
- [ ] Wiki page view component (`/wiki/Page_Name`)
- [ ] Wiki editor component (Markdown textarea + preview)
- [ ] Wiki history/diff view
- [ ] Recent changes list
- [ ] Namespace browsing (all Character: pages, all Help: pages)

## Testing
- [ ] Markdig pipeline: all extensions render correctly
- [ ] Wiki-link resolution: existing pages, broken links (redlinks)
- [ ] Revision history: create, view diff, rollback
- [ ] Permission checks: owner edit, royalty edit any, wizard delete
- [ ] @wiki commands produce correct MString output
