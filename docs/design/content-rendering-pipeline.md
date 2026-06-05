# Content Rendering Pipeline

## Overview

Content rendering in SharpMUSH uses a shared library approach. MString already
provides `ToAnsi()` and `ToHtml()` conversion methods. Markdown rendering uses
Markdig (same library on server and client). No duplication — one path per format.

## Rendering Paths

```
                    ┌──────────────┐
                    │   MString    │  (game output, poses, descriptions)
                    └──────┬───────┘
                           │
              ┌────────────┼────────────┐
              ▼            ▼            ▼
         .ToAnsi()    .ToHtml()    .ToPlainText()
              │            │            │
              ▼            ▼            ▼
         Telnet/SSH    Web portal    Search index
         clients       rendering     full-text


                    ┌──────────────┐
                    │   Markdown   │  (wiki pages, profile freeform, help)
                    └──────┬───────┘
                           │
              ┌────────────┼────────────┐
              ▼            ▼            ▼
         Markdig →     Markdig →    Strip to
         HTML          MString*     plain text
              │            │            │
              ▼            ▼            ▼
         Web portal    In-game       Search index
         wiki view     @wiki/view    full-text

   * Markdown → MString = Markdig parse → custom renderer that emits ANSI
     (bold → ANSI bold, headers → colored, links → MXP clickable, etc.)
```

## Shared Library: MString

MString is the game's native rich text type. It already lives in a shared
project accessible to both server and client code.

**Existing methods (already implemented):**
- `MString.ToAnsi()` → ANSI escape sequences (for telnet/terminal)
- `MString.ToHtml()` → HTML spans with inline styles or CSS classes
- `MString.ToPlainText()` → stripped plain text (for search, logging)

**Web portal usage:**
- Scene panel: receives MString from SignalR → calls `.ToHtml()` client-side
- Scene archive: server renders `.ToHtml()` for SSR/SEO
- Profile structured fields (format: "mstring"): same `.ToHtml()` path

**No client-side ANSI parsing library needed.** MString handles it natively.
The Blazor WASM client references the same shared library the server uses.

## Markdown Rendering: Markdig

Markdig is used everywhere Markdown appears. Same library, same extensions,
same configuration — server and client.

**Markdig pipeline configuration:**

```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UseAdvancedExtensions()      // Tables, footnotes, task lists
    .UseAutoLinks()               // Auto-detect URLs
    .UseEmojiAndSmiley()          // :emoji: shortcodes
    .UsePipeTables()              // GFM-style tables
    .UseAutoIdentifiers()         // Header anchors for linking
    .DisableHtml()                // No raw HTML in user content
    .Build();
```

**Key: `DisableHtml()` is always on for user content.** Raw HTML tags become
literal text. The only exception is admin pages with `AllowHtml` flag
(per architectural decision 5.4).

### Wiki-Links Extension (Custom)

A custom Markdig extension resolves `[[wiki links]]`:

```
[[Page Name]]           → <a href="/wiki/Page_Name">Page Name</a>
[[Character:Gandalf]]   → <a href="/wiki/Character:Gandalf">Gandalf</a>
[[Page Name|display]]   → <a href="/wiki/Page_Name">display</a>
```

Resolution happens at render time. Broken links get a "redlink" CSS class
(styled differently — indicates page doesn't exist yet, clickable to create).

### Markdown → MString (In-Game Rendering)

For `@wiki/view` in-game, Markdown is converted to MString:

```
# Header          → %ch%cuHeader%cn (bold + color)
**bold**          → %chbold%cn
*italic*          → (no ANSI italic on most clients — rendered as-is or underline)
[link](url)       → MXP: \e[4m\e]link\a (clickable if MXP-capable client)
`code`            → %ch%cgcode%cn (bold green or configurable)
> blockquote      → | prefixed lines (indented with color)
- list item       → • prefixed
| table |         → formatted with borders (fixed-width aligned)
```

This is a custom Markdig renderer (implements `IMarkdownRenderer` or walks
the AST) that emits MString segments instead of HTML.

## Content Contexts

### Poses (MString only)

```
Storage:  MString (raw, with ANSI markup codes)
Web:      MString.ToHtml() → HTML in scene panel
In-game:  MString.ToAnsi() → native terminal output
Search:   MString.ToPlainText() → indexed text
```

No Markdown processing. Poses are MString, period (Decision 7.4).

### Wiki Pages (Markdown)

```
Storage:  Markdown source text (plain UTF-8)
Web:      Markdig → HTML (with wiki-link extension)
In-game:  Markdig → MString (custom renderer)
Search:   Markdig → plain text (strip all formatting)
```

### Profile Structured Fields

```
Default:     Plain text (no processing, rendered as-is)
format=mstring:  MString.ToHtml() for web, MString.ToAnsi() for game
format=markdown: Markdig → HTML for web, Markdig → MString for game
```

### Help Files

```
Storage:  Markdown source
Web:      Markdig → HTML (same as wiki)
In-game:  Markdig → MString (same as wiki)
```

### Game Command Output (terminal panel)

```
Source:   MString (game engine output)
Web:      MString.ToHtml() → terminal panel
```

The terminal panel renders ALL game output as HTML-from-MString. It doesn't
interpret Markdown — it's a faithful terminal emulator showing exactly what
a telnet client would see, but with HTML styling instead of ANSI escapes.

## Image Handling

### In Markdown (wiki pages, profiles)

```markdown
![alt text](image-url)
![alt text](image-url "caption")
```

Rendered as:
- Web: `<img>` with lazy loading (`loading="lazy"`), max-width constraint,
  click-to-lightbox (MudBlazor `MudImage` + overlay)
- In-game: `[Image: alt text - image-url]` (plain text reference, or MXP
  clickable link to open in browser)

### Gallery Images (profile)

Not Markdown — rendered by a dedicated Blazor component. Thumbnails grid,
click to lightbox, drag to reorder (admin/owner only).

### Upload Limits

- Max file size: configurable (default 5MB)
- Allowed types: jpg, png, gif, webp
- Stored via IFileStorage interface
- Served with cache headers (immutable hash-named files)

## Sanitization Summary

| Content Type      | Input Format | Sanitization Rule              |
|-------------------|--------------|--------------------------------|
| Poses             | MString      | None needed (MString is safe)  |
| Wiki pages        | Markdown     | DisableHtml() — no raw HTML    |
| Profile freeform  | Markdown     | DisableHtml() — no raw HTML    |
| Profile fields    | Plain/MString| MString.ToHtml() is safe       |
| Help files        | Markdown     | DisableHtml() — no raw HTML    |
| Admin pages       | Markdown+HTML| AllowHtml flag (trusted only)  |

**Why MString is safe:** MString.ToHtml() produces escaped output. ANSI codes
map to specific CSS classes/spans — there's no path from ANSI input to arbitrary
HTML injection. The conversion is a closed function.

**Why DisableHtml() is sufficient:** Markdig with DisableHtml() turns all `<tags>`
into `&lt;tags&gt;`. Combined with MString safety, there is no XSS vector in
user-generated content.

## Performance Considerations

- **Wiki rendering is cached:** rendered HTML stored alongside Markdown source.
  Invalidated on edit. No re-render on every page view.
- **Scene poses rendered client-side:** MString.ToHtml() runs in WASM. Keeps
  server load low for real-time scene streaming.
- **Scene archives rendered server-side:** for SSR/SEO. Cached after first render.
- **Search indexing:** plain text extraction runs once on write (stored in
  `text_plain` field). Not re-extracted on every search query.
