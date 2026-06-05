# Area 8: Content Rendering Pipeline — TODO

## Pre-Implementation
- [ ] Review & confirm decisions (8.1–8.5) with project owner
- [ ] Identify any decisions that need revision based on current codebase state

## Implementation Tasks
- [ ] Verify MString.ToHtml() works correctly for all ANSI codes in use
- [ ] Verify MString.ToPlainText() strips all formatting cleanly
- [ ] Configure Markdig pipeline (shared between server and WASM client)
- [ ] Implement DisableHtml() enforcement on all user content paths
- [ ] Implement wiki-link Markdig extension (resolve [[links]], redlink CSS class)
- [ ] Implement Markdown → MString custom renderer (for @wiki/view in-game)
  - Headers → bold + colored MString
  - Bold/italic → ANSI equivalents
  - Links → MXP clickable (if client supports) or plain text fallback
  - Code → bold green (configurable)
  - Blockquotes → pipe-prefixed, indented
  - Lists → bullet-prefixed
  - Tables → fixed-width aligned with borders
- [ ] Wire up rendering in scene panel (MString.ToHtml() client-side in WASM)
- [ ] Wire up rendering in scene archives (server-side for SSR/SEO)
- [ ] Wiki rendered HTML caching (store alongside source, invalidate on edit)
- [ ] Plain text extraction on write (for search indexing)
- [ ] Image handling in Markdown (lazy loading, max-width, lightbox on web; text fallback in-game)

## Testing
- [ ] MString.ToHtml(): all ANSI codes produce correct HTML spans/classes
- [ ] MString.ToHtml(): no XSS possible from any MString input
- [ ] Markdig + DisableHtml(): raw HTML tags become escaped text
- [ ] Wiki-link extension: valid pages, broken pages, namespaced pages, display text
- [ ] Markdown → MString: headers, bold, links, code, lists, tables all render sensibly
- [ ] Cache invalidation: edit wiki page → next read gets fresh render
