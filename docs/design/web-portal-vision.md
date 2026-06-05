# SharpMUSH Web Portal — Design Vision

## Overview

The SharpMUSH Web Portal is a Blazor WASM application (MudBlazor 9.x) that provides
a holistic, modern interface to the game. It is not merely a telnet-in-a-browser; it
is a full-featured game portal encompassing wiki, scenes, characters, communication,
and administration — all backed by the same database the game engine uses.

Key differentiators over AresMUSH and other MU* web portals:
- **Layout customization** — admins (and optionally players) rearrange the portal structure
- **Theming** — full visual customization (colors, typography, imagery) from an admin UI
- **Real-time first** — SignalR for structured events; raw WebSocket for terminal/MXP
- **Wiki as shared content** — wiki pages readable and navigable from both web and in-game
- **Widget architecture** — features are composable units that can be placed in layout slots

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                      SharpMUSH.Client                            │
│                   (Blazor WASM + MudBlazor)                      │
├─────────────────────────────────────────────────────────────────┤
│  Core Services:                                                  │
│    LayoutService     — config-driven layout rendering            │
│    ThemeService      — DB-backed MudTheme + CSS variables        │
│    WidgetRegistry    — maps widget IDs → Blazor component Types  │
│                                                                  │
│  Feature Services:                                               │
│    WikiService       — CRUD wiki pages (Markdown, DB-backed)     │
│    SceneService      — real-time scene participation             │
│    ChatService       — channel subscriptions via SignalR          │
│    ProfileService    — character data from game objects           │
│    MailService       — inbox/compose/send                        │
│    BBSService        — forum boards, posts                       │
│    CalendarService   — events CRUD + iCal export                 │
│                                                                  │
│  Real-time:                                                      │
│    SignalR Hub (/hub/game) — scenes, chat, presence, wiki edits  │
│    Raw WebSocket (/ws)    — terminal/MXP/ANSI game protocol      │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                      SharpMUSH.Server                            │
│                   (ASP.NET Core + SignalR)                        │
├─────────────────────────────────────────────────────────────────┤
│  API Controllers:                                                │
│    /api/layout           — layout config CRUD                    │
│    /api/theme            — theme config CRUD                     │
│    /api/wiki/{slug}      — wiki page CRUD                        │
│    /api/scenes           — scene list, join, pose                │
│    /api/characters/{id}  — profile data                          │
│    /api/mail             — inbox, send, folders                  │
│    /api/bbs              — boards, threads, posts                │
│    /api/events           — calendar CRUD + iCal feed             │
│    /api/presence         — who's online                          │
│                                                                  │
│  SignalR Hubs:                                                    │
│    /hub/game             — multiplexed real-time events           │
│                                                                  │
│  Internal: NATS pub/sub for cross-service messaging              │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                      Game Engine                                  │
│              (Parser, Visitor, Commands, DB)                      │
├─────────────────────────────────────────────────────────────────┤
│  Wiki content stored as game objects (or dedicated wiki nodes)    │
│  Accessible via softcode: wiki(), wikiset(), wikilist()          │
│  Changes propagate via NATS → SignalR → web clients              │
└─────────────────────────────────────────────────────────────────┘
```

---

## Wiki as Shared Content

### Problem Statement

Traditional MU* games have two disconnected help/content systems:
1. In-game `help` files (flat text, no formatting, searched by topic name)
2. External wikis (Wikidot, MediaWiki, or Ares's built-in wiki)

Players must context-switch between them. Content authors maintain two copies.
AresMUSH improved this by having its wiki pull some data from the game, but the
wiki itself is web-only — you cannot read wiki pages from within the game client.

### Design: Unified Content Layer

SharpMUSH treats wiki pages as **game-world content** — stored in the database,
accessible from both the web portal (rendered as HTML from Markdown) and from
within the game (rendered as ANSI-formatted text from the same Markdown source).

#### Storage Model

Wiki pages are stored as dedicated nodes in the graph database:

```
WikiPage {
    slug: string          // URL-friendly identifier (e.g., "character-creation")
    title: string         // Display title
    body: string          // Markdown content (source of truth)
    category: string      // Grouping (e.g., "rules", "lore", "systems")
    tags: string[]        // Searchable tags
    author: DBRef         // Creator
    lastEditor: DBRef     // Last person to edit
    createdAt: DateTime
    updatedAt: DateTime
    locked: bool          // Only admins can edit
    published: bool       // Visible to non-admins
    sortOrder: int        // Within category
}
```

Edges connect wiki pages to each other (links) and to game objects (references).

#### Web Rendering (Portal)

- Markdown → HTML via Markdig (already a .NET library)
- MudBlazor components for layout (MudCard, MudBreadcrumbs, table of contents)
- Live editing with split-pane preview
- Syntax highlighting for code blocks
- Image/file attachments (stored as blobs or linked)
- Inter-page links: `[[page-slug]]` or `[[page-slug|Display Text]]`
- Dynamic includes: `{{characters:online}}` pulls live data
- Category/tag browsing, full-text search

#### In-Game Rendering (Terminal)

The same Markdown source is rendered to ANSI text for terminal clients:

```
Markdown             →  ANSI Terminal
─────────────────────────────────────────
# Heading            →  [bold][cyan]HEADING[/]
## Subheading        →  [bold]Subheading[/]
**bold**             →  [bold]bold[/]
*italic*             →  [underline]italic[/] (terminals lack true italic)
`code`               →  [yellow]code[/]
[link](url)          →  link (url)  — or MXP <send> tag if MXP enabled
- list item          →  • list item
1. numbered          →  1. numbered
> blockquote         →  │ blockquote (with indent + border char)
---                  →  ════════════════════════════════════
[[other-page]]       →  other-page (type 'wiki other-page' to read)
```

For MXP-enabled clients, links become clickable `<send>` tags:
```
[[character-creation|CharGen Guide]]
→ MXP: <send href="wiki character-creation">CharGen Guide</send>
→ Plain: CharGen Guide (wiki character-creation)
```

#### Softcode Interface

The wiki is exposed as hardcode **functions** (not commands). Softcode authors
build game-specific commands on top. See `wiki-shared-content.md` for the full
function list. Key functions:

```
Functions (hardcode primitives):
  wiki(<slug>)              — Returns raw Markdown content of a page
  wiki(<slug>, field)       — Returns a specific field (title, author, etc.)
  wikilist([category])      — Returns space-separated list of slugs
  wikisearch(<terms>)       — Returns matching slugs
  wikiset(<slug>, field, value) — Set a field (permission-gated)
  wikicreate(<slug>, <title>, <body>[, <category>])
  wikidelete(<slug>)        — Admin only
  wikirender(<slug>)        — Returns ANSI-rendered MString
  wikicategories()          — All category keys

Hardcode commands (@wiki family):
  @wiki <slug>              — Display ANSI-rendered page
  @wiki/list [category]     — List pages
  @wiki/search <terms>      — Full-text search
  @wiki/set <slug>/<field>=<value> — Set a field
  @wiki/create <slug>=<title>     — Create a page
  @wiki/delete <slug>       — Delete a page (admin only)
  @wiki/history <slug>      — Revision history
  @wiki/revert <slug>=<rev> — Revert (admin only)
  @wiki/categories          — List categories
```

Games build their own player-facing UX via softcode $commands calling the
function primitives. The `@wiki` family is the built-in admin/builder tool.
Example: `+lore <topic>` → `wikirender(lore/<topic>)`

#### Synchronization

Changes propagate in real-time:
- Web edit → save to DB → NATS publish "wiki.updated:{slug}" → 
  - SignalR pushes to open wiki viewers (live reload)
  - Game engine cache invalidated (next `+wiki` shows fresh content)
- In-game edit → save to DB → NATS publish → SignalR push to web viewers

#### Permissions

Wiki permissions align with the game's permission system:
- `WIZARD` / `ROYALTY` — can edit locked pages, delete pages
- `BUILDER` — can create/edit unlocked pages
- Any authenticated player — can edit unlocked pages (if wiki is "open edit")
- Unauthenticated web visitors — read-only access to published pages

The game's `@lock` system can be extended:
```
@lock/wiki <page-object>=<lock-expression>
```

#### Migration from Help Files

Existing PennMUSH-style `help.txt` files can be bulk-imported:
- Each help topic becomes a wiki page
- `& topic-name` headers → slug + title
- Cross-references (`See: other-topic`) → wiki links
- Category assigned by source file (help vs news vs rules)

---

## Layout System

### Slot Architecture

The page is divided into named slots:

```
┌─────────────────────────────────────────────────────────────┐
│                        TopBar                                 │
│  [left: branding]  [center: custom-links]  [right: actions] │
├──────────┬────────────────────────────────────┬─────────────┤
│          │                                    │             │
│  Left    │          MainContent               │   Right     │
│  Sidebar │          (routed page)             │   Sidebar   │
│          │                                    │             │
│          │                                    │             │
├──────────┴────────────────────────────────────┴─────────────┤
│                      BottomBar                               │
│              [terminal / status / quick-actions]              │
├─────────────────────────────────────────────────────────────┤
│                        Footer                                │
└─────────────────────────────────────────────────────────────┘
```

### Layout Configuration (stored in DB)

```json
{
  "layoutName": "default",
  "topBar": {
    "left": ["branding"],
    "center": ["custom-links"],
    "right": ["language-picker", "character-switcher", "terminal-toggle", "login"]
  },
  "leftSidebar": {
    "enabled": true,
    "width": "250px",
    "collapsible": true,
    "widgets": ["nav-links", "online-players"]
  },
  "rightSidebar": {
    "enabled": false,
    "width": "300px",
    "collapsible": true,
    "widgets": []
  },
  "bottomBar": {
    "enabled": true,
    "mode": "terminal",
    "height": "300px"
  },
  "footer": {
    "enabled": true,
    "widgets": ["custom-html", "powered-by"]
  },
  "navLinks": [
    {"label": "Home", "href": "/", "icon": "Home", "slot": "left"},
    {"label": "Wiki", "href": "/wiki", "icon": "MenuBook", "slot": "left"},
    {"label": "Characters", "href": "/characters", "icon": "People", "slot": "left"},
    {"label": "Scenes", "href": "/scenes", "icon": "TheaterComedy", "slot": "left"},
    {"label": "Forums", "href": "/bbs", "icon": "Forum", "slot": "left"},
    {"label": "Events", "href": "/events", "icon": "Event", "slot": "left"}
  ]
}
```

### Widget Registry

```csharp
public static class WidgetRegistry
{
    private static readonly Dictionary<string, Type> _widgets = new()
    {
        ["branding"]           = typeof(BrandingWidget),
        ["nav-links"]          = typeof(NavLinksWidget),
        ["custom-links"]       = typeof(CustomLinksWidget),
        ["online-players"]     = typeof(OnlinePlayersWidget),
        ["character-summary"]  = typeof(CharacterSummaryWidget),
        ["quick-channel"]      = typeof(QuickChannelWidget),
        ["recent-scenes"]      = typeof(RecentScenesWidget),
        ["wiki-search"]        = typeof(WikiSearchWidget),
        ["custom-html"]        = typeof(CustomHtmlWidget),
        ["language-picker"]    = typeof(LanguagePicker),
        ["character-switcher"] = typeof(CharacterSwitcherWidget),
        ["terminal-toggle"]    = typeof(TerminalToggleWidget),
        ["login"]              = typeof(LoginDisplay),
        ["powered-by"]         = typeof(PoweredByWidget),
    };

    public static Type? Resolve(string widgetId) 
        => _widgets.GetValueOrDefault(widgetId);

    public static void Register(string id, Type componentType) 
        => _widgets[id] = componentType;
}
```

### Admin Layout Editor

A visual editor page (`/admin/layout`) using MudDropZone:
- Shows a miniature representation of the page layout
- Each slot is a drop zone
- Available widgets listed in a palette on the side
- Drag widgets into slots, reorder within slots
- Toggle sidebars on/off
- Set widths, toggle collapsible behavior
- Save → writes layout JSON to DB
- Preview button opens a new tab with the layout applied

### Per-User Customization (colors only)

Layout customization is **admin-only**. Players cannot rearrange widgets or
sidebars. However, players CAN customize their visual theme:

- Dark/light mode toggle
- Color palette overrides (primary, secondary, background tints)
- Font size preference (small/medium/large)
- Terminal font family choice (from a curated list)

Stored per-account in the DB. These are CSS variable overrides layered on top of
the admin's chosen theme — they don't affect layout or widget placement.

```csharp
public record PlayerThemePreferences
{
    public bool? DarkMode { get; init; }              // null = follow admin default
    public string? PrimaryColor { get; init; }        // "#hex" or null for default
    public string? SecondaryColor { get; init; }
    public string? AccentColor { get; init; }
    public string? FontSize { get; init; }            // "small", "medium", "large"
    public string? TerminalFontFamily { get; init; }  // from allowed list
}
```

---

## Theming System

### Theme Configuration (DB-backed)

```json
{
  "name": "cyberpunk-neon",
  "darkMode": true,
  "palette": {
    "primary": "#00f5b7",
    "secondary": "#ff00ff",
    "tertiary": "#00bfff",
    "background": "#0a0a0f",
    "surface": "#1a1a2e",
    "appBar": "#0f0f1a",
    "textPrimary": "rgba(255, 255, 255, 0.92)",
    "textSecondary": "rgba(255, 255, 255, 0.60)",
    "error": "#ff4444",
    "warning": "#ffaa00",
    "success": "#00ff88"
  },
  "typography": {
    "fontFamily": "'JetBrains Mono', 'Fira Code', monospace",
    "headingFontFamily": "'Inter', sans-serif",
    "baseFontSize": "14px"
  },
  "shape": {
    "borderRadius": "6px",
    "cardElevation": 2
  },
  "branding": {
    "gameName": "My MUSH",
    "logoUrl": "/uploads/logo.png",
    "headerImageUrl": "/uploads/header-bg.jpg",
    "faviconUrl": "/uploads/favicon.ico"
  }
}
```

### Theme Editor (admin page)

- Color pickers with live preview (pick color → page updates instantly)
- Typography section (font selector, size slider)
- Shape section (border radius slider, elevation selector)
- Branding section (name, logo upload, header image)
- Import/Export as JSON
- "Presets" dropdown with built-in themes (Cyberpunk, Forest, Ocean, Classic)

### Runtime Application

```csharp
// ThemeService loads from API on app init, caches in memory
public class ThemeService
{
    public MudTheme CurrentTheme { get; private set; }
    
    public async Task LoadAsync()
    {
        var config = await _httpClient.GetFromJsonAsync<ThemeConfig>("/api/theme");
        CurrentTheme = BuildMudTheme(config);
    }
    
    private MudTheme BuildMudTheme(ThemeConfig config) => new()
    {
        PaletteDark = new PaletteDark
        {
            Primary = config.Palette.Primary,
            Secondary = config.Palette.Secondary,
            // ... etc
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = [config.Typography.FontFamily] }
        }
    };
}
```

---

## Implementation Phases

### Phase 1: Foundation (current + near-term)
- [x] Terminal with WebSocket + multi-character
- [x] Admin panel (config, import, banned names, sitelock)
- [x] Wiki components (WikiView, WikiEdit, WikiDisplay)
- [ ] Wiki API endpoints + DB storage
- [ ] Wiki in-game commands (+wiki, wiki(), etc.)
- [ ] Markdown-to-ANSI renderer for terminal output
- [ ] ThemeService (extract hardcoded MudTheme → DB-backed)
- [ ] LayoutService (extract hardcoded MainLayout → config-driven)

### Phase 2: Portal Features
- [ ] Character profiles page (attribute-driven, auto-sync)
- [ ] Scene system (list, join, pose via SignalR)
- [ ] BBS/Forums page
- [ ] Mail inbox page
- [ ] Online players widget + presence tracking
- [ ] Help file → wiki migration tool

### Phase 3: Layout Engine
- [ ] WidgetRegistry + DynamicComponent rendering
- [ ] Admin layout editor (MudDropZone)
- [ ] Per-user layout preferences
- [ ] Custom nav links management

### Phase 4: Polish & Community
- [ ] Theme editor with live preview + presets
- [ ] Events/calendar with iCal
- [ ] Wiki full-text search
- [ ] Wiki revision history + diff view
- [ ] Custom widget support (softcode-driven content blocks)
- [ ] RSS feeds for scenes, wiki changes, forum posts

---

## Design Decisions (Resolved)

1. **Wiki storage**: Separate document collection — NOT graph nodes mixed with
   game objects. Wiki pages have their own schema and query patterns. They can
   reference game objects via DBRef fields, but they don't live in the object
   graph. This keeps the object namespace clean and wiki queries fast.

2. **Multi-character model**: Each browser tab is its own Blazor WASM instance,
   connected as one character. No multi-tab terminal within a single WASM app.
   If a player wants to play two characters simultaneously, they open two browser
   tabs. Each tab has its own WebSocket connection, its own session state, its own
   terminal. This is simpler architecturally and matches how browser sessions work.

3. **Wiki commands**: NOT `+wiki` (that implies softcode/addon). The wiki is
   exposed as backing functions and hardcode infrastructure — similar to how
   `textentries()` works. Softcode authors build their own commands on top.
   Examples: a game might create `+lore <topic>` that calls `wiki(lore/<topic>)`
   or `+rules` that calls `wikilist(rules)`. The system provides the primitives;
   softcode provides the player-facing UX. A basic `wiki <slug>` or `@wiki <slug>`
   hardcode command may exist as a minimal default, but the real power is in the
   function layer.

4. **Dynamic includes** (`{{character:Name:attr}}`, `{{online-players}}`):
   Deferred to a future phase. The wiki system ships with static Markdown +
   wiki-links first. Dynamic content blocks are a later enhancement once the
   base system is proven.

5. **Layout customization**: Admin-only. Players do NOT rearrange widgets or
   sidebars. However, players CAN customize their theme colors (palette choices,
   dark/light mode, font size) — stored per-account.

## Open Questions (Remaining)

1. **SignalR vs. extending the existing WS protocol**: The terminal already uses
   WebSocket with a custom protocol. Should SignalR be a separate connection, or
   should structured messages ride on the same socket? Separate is cleaner for
   the web portal; same socket is more efficient for resource-constrained clients.

2. **Wiki editing permissions model**: Open wiki (any player can edit any unlocked
   page) vs. tiered (builders+ create, players suggest edits)? Should this be
   configurable per-game?

3. **Markdown flavor**: Which extensions to support? Tables, task lists, footnotes,
   definition lists, emoji shortcodes? Markdig supports all of these via extension
   pipeline — which are worth the complexity of ANSI-rendering them in-game?
