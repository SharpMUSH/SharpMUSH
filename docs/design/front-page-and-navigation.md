# Front Page & Navigation Design

## Design Goals

1. **Instant orientation** — a new visitor understands what this game is and how
   to get started within 5 seconds of landing
2. **Omnisearch** — one search bar to find everything (wiki, characters, help,
   scenes, commands) without knowing where things live
3. **Progressive disclosure** — unauthenticated visitors see public content;
   logged-in players see their character's world; admins see tools
4. **Zero-training UI** — no "how do I use this site" page needed; the layout
   itself teaches through clear labels, contextual hints, and smart defaults

## Front Page Layout (Unauthenticated Visitor)

```
┌─────────────────────────────────────────────────────────────────────┐
│ TopBar: [Logo/GameName]         [Omnisearch: Ctrl+K]    [Login/Register] │
├─────────────────────────────────────────────────────────────────────┤
│                                                                       │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │                    HERO SECTION                                │    │
│  │  Game name (large), tagline, header image/art                 │    │
│  │  [Connect & Play]  [Read the Wiki]  [Browse Characters]       │    │
│  └─────────────────────────────────────────────────────────────┘    │
│                                                                       │
│  ┌──────────────────────┐  ┌──────────────────────────────────┐    │
│  │  GETTING STARTED     │  │  GAME AT A GLANCE                  │    │
│  │  • What is this?     │  │  • Players online: 12               │    │
│  │  • How to connect    │  │  • Active scenes: 3                 │    │
│  │  • New player guide  │  │  • Wiki pages: 247                  │    │
│  │  • Rules summary     │  │  • Last scene: 2h ago               │    │
│  └──────────────────────┘  └──────────────────────────────────┘    │
│                                                                       │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │  RECENT ACTIVITY (public)                                      │    │
│  │  • Scene posted: "The Market Square" (3 participants, 2h ago)  │    │
│  │  • Wiki updated: "Magic System" (yesterday)                    │    │
│  │  • New character: Aldric the Wanderer (approved today)         │    │
│  └─────────────────────────────────────────────────────────────┘    │
│                                                                       │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │  WELCOME TEXT (admin-configurable Markdown block)               │    │
│  │  Renders wiki page: "home" or configurable slug                 │    │
│  └─────────────────────────────────────────────────────────────┘    │
│                                                                       │
├─────────────────────────────────────────────────────────────────────┤
│ Footer: [Powered by SharpMUSH] [Privacy] [Code of Conduct]           │
└─────────────────────────────────────────────────────────────────────┘
```

### Key Design Choices

- **No left sidebar for visitors** — clean, focused landing. Sidebar appears
  after login (or if admin enables it for public).
- **Hero section is a widget** — admin can replace it with custom HTML, a
  different image, or disable it entirely.
- **"Getting Started" links to wiki pages** — these are just wiki slugs
  (`getting-started`, `how-to-connect`, `rules`). Admin configures which
  slugs appear here. No hardcoded content.
- **"Game at a Glance" is live** — pulls from the presence API and wiki/scene
  counts. Shows the game is active and alive.
- **Welcome text = wiki page** — the admin sets a "home page wiki slug" in
  config. This means the home page content is editable from both web AND
  in-game via `@wiki/set home/body=...`.

## Front Page Layout (Authenticated Player)

```
┌─────────────────────────────────────────────────────────────────────┐
│ TopBar: [Logo]  [Wiki] [Scenes] [Characters] [Omnisearch: Ctrl+K]    │
│         [Mail (3)] [Notifications (1)] [Avatar ▾]                     │
├──────────┬──────────────────────────────────────────────────────────┤
│ Left Nav │                                                            │
│          │  WELCOME BACK, <Character Name>                            │
│ • Home   │                                                            │
│ • Wiki   │  ┌─────────────────┐  ┌─────────────────────────┐       │
│ • Scenes │  │ YOUR CHARACTER   │  │ ACTIVE SCENES             │       │
│ • Chars  │  │ Portrait/avatar  │  │ • The Market (3 ppl, live)│       │
│ • Mail   │  │ Name, status     │  │ • Council (2 ppl, 10m)    │       │
│ • Events │  │ [Edit Profile]   │  │ • [Start New Scene]       │       │
│ • Forums │  └─────────────────┘  └─────────────────────────────┘   │
│          │                                                            │
│ ─────── │  ┌─────────────────────────────────────────────────┐     │
│ • Admin  │  │ RECENT ACTIVITY (personalized)                    │     │
│          │  │ • Mail from Gandalf: "About tomorrow..." (1h ago) │     │
│          │  │ • Scene you're in updated (The Market, 30m ago)   │     │
│          │  │ • Wiki page you watch edited: "Combat" (2h ago)   │     │
│          │  │ • Event tomorrow: "Story Night" at 8pm             │     │
│          │  └─────────────────────────────────────────────────┘     │
│          │                                                            │
│          │  ┌──────────────────────┐  ┌───────────────────────┐    │
│          │  │ GAME AT A GLANCE      │  │ QUICK ACTIONS           │    │
│          │  │ Online: 12            │  │ [Open Terminal]          │    │
│          │  │ Active scenes: 3      │  │ [New Scene]              │    │
│          │  │ Your unread mail: 3   │  │ [Write Wiki Page]        │    │
│          │  │ Upcoming events: 1    │  │ [View Events]            │    │
│          │  └──────────────────────┘  └───────────────────────┘    │
│          │                                                            │
├──────────┴──────────────────────────────────────────────────────────┤
│ BottomBar: [Terminal toggle ▲] — collapsed by default on Home         │
└─────────────────────────────────────────────────────────────────────┘
```

### Key Differences from Visitor View

- **Left sidebar appears** with navigation links (configurable by admin)
- **Personalized dashboard** — your character, your unread mail, your scenes
- **Quick actions** — one-click access to the most common tasks
- **Terminal collapsed** — it's always available via toggle, but doesn't
  dominate the home page. Expands on the Play/Terminal route.
- **Admin link** — only visible to admin-flagged accounts

---

## Omnisearch (Ctrl+K / Cmd+K)

### Concept

A single, unified search interface — inspired by VS Code's command palette,
GitHub's Ctrl+K, and Linear's spotlight. One input searches EVERYTHING:

- Wiki pages (by title, body content, tags)
- Characters (by name)
- Help topics (traditional help entries)
- Scenes (by title, participants)
- Forum/BBS posts (by title, body)
- Navigation (page names: "Settings", "Admin", "Events")
- Commands (for admins: "Clear cache", "Toggle maintenance")

### Why This Matters

MU* portals (including Ares) scatter content across separate search boxes or
have no cross-cutting search at all. A player looking for "combat" might need
to check:
- The wiki for rules
- Help files for command syntax
- Scenes for past combat RP
- Characters for combat-focused players

Omnisearch collapses all of that into one place.

### UX Behavior

```
┌─────────────────────────────────────────────────────────┐
│  🔍  Search anything... (Ctrl+K)                          │
├─────────────────────────────────────────────────────────┤
│                                                           │
│  (empty state — shows recent + suggested)                │
│                                                           │
│  RECENT                                                   │
│    📄 Combat Rules                         wiki           │
│    👤 Aldric the Wanderer                  character      │
│    🎭 The Market Square                    scene          │
│                                                           │
│  SUGGESTED                                                │
│    📖 Getting Started Guide                wiki           │
│    ⚙️  Account Settings                    navigation     │
│                                                           │
└─────────────────────────────────────────────────────────┘
```

After typing:

```
┌─────────────────────────────────────────────────────────┐
│  🔍  combat                                               │
├─────────────────────────────────────────────────────────┤
│                                                           │
│  WIKI (3 results)                                        │
│    📄 Combat Rules              rules/combat    ⏎ open   │
│    📄 Magic in Combat           rules/magic     ⏎ open   │
│    📄 Character Creation        ...mentions combat...     │
│                                                           │
│  HELP (1 result)                                         │
│    ❓ +combat command            help/combat     ⏎ open   │
│                                                           │
│  CHARACTERS (2 results)                                  │
│    👤 Sir Marcus (combat focus)                  ⏎ view   │
│    👤 Elena Brightblade                          ⏎ view   │
│                                                           │
│  SCENES (1 result)                                       │
│    🎭 "Battle at Dawn" (last week)              ⏎ read   │
│                                                           │
│  ─── Press Tab to filter by type ───                     │
│                                                           │
└─────────────────────────────────────────────────────────┘
```

### Keyboard Navigation

| Key        | Action                                          |
|------------|-------------------------------------------------|
| Ctrl+K     | Open omnisearch (from anywhere on the site)     |
| Escape     | Close omnisearch                                |
| ↑ / ↓     | Navigate results                                |
| Enter      | Open selected result                            |
| Tab        | Cycle category filter (All → Wiki → Help → ...) |
| Ctrl+Enter | Open in new tab                                 |

### Type Prefixes (Power User)

Typing a prefix narrows results immediately:

| Prefix     | Searches only...         | Example               |
|------------|--------------------------|------------------------|
| `wiki:`    | Wiki pages               | `wiki:combat`          |
| `help:`    | Help entries             | `help:+who`            |
| `char:`    | Characters               | `char:aldric`          |
| `scene:`   | Scenes                   | `scene:market`         |
| `@`        | Navigation/commands      | `@settings`            |
| `>`        | Admin commands           | `>clear cache`         |

### Implementation (MudBlazor)

```csharp
// Uses MudOverlay + MudTextField + virtualized MudList
// Renders as a modal dialog centered on screen

public class OmnisearchService
{
    // Federated search — queries multiple providers in parallel
    private readonly IReadOnlyList<ISearchProvider> _providers;
    
    public async Task<OmnisearchResults> SearchAsync(
        string query, 
        SearchScope? scope = null,
        CancellationToken ct = default)
    {
        var tasks = _providers
            .Where(p => scope is null || p.Scope == scope)
            .Select(p => p.SearchAsync(query, limit: 5, ct));
        
        var results = await Task.WhenAll(tasks);
        return new OmnisearchResults(results.SelectMany(r => r));
    }
}

public interface ISearchProvider
{
    SearchScope Scope { get; }           // Wiki, Help, Characters, etc.
    string Icon { get; }                  // MudBlazor icon
    Task<IEnumerable<SearchResult>> SearchAsync(
        string query, int limit, CancellationToken ct);
}

public record SearchResult(
    string Title,
    string Subtitle,          // snippet, category, etc.
    string Url,               // where to navigate
    SearchScope Scope,
    float Relevance           // for sorting across providers
);
```

### Search Providers (pluggable)

| Provider           | Source                  | Index type        |
|--------------------|------------------------|-------------------|
| WikiSearchProvider  | /api/wiki/search       | FTS (DB-native)   |
| HelpSearchProvider  | /api/help/search       | In-memory prefix  |
| CharSearchProvider  | /api/characters/search | DB name field     |
| SceneSearchProvider | /api/scenes/search     | FTS on title+body |
| NavSearchProvider   | Client-side routes     | In-memory fuzzy   |
| AdminCmdProvider    | Client-side commands   | In-memory fuzzy   |

Each provider is independent. New features (BBS, Events) register their own
provider. The omnisearch service just federates across whatever's registered.

---

## Contextual Help & Discoverability

### Problem

"How do I find help?" is the first question every new user has. If the answer
is "click the Wiki link and search for a getting started page" — you've already
lost them. Help must be ambient, not a destination.

### Solution: Layered Help System

**Layer 1: Inline hints (zero-click)**
- Empty states show helpful text: "No scenes yet. Start one?"
- Input fields have placeholder text that explains their purpose
- New users see a dismissible "first-visit" banner on key pages

**Layer 2: Contextual ? icons (one-click)**
- A small `?` icon next to complex UI elements opens a tooltip or small
  popover explaining that feature
- Links to the relevant wiki page for deeper reading
- Uses MudTooltip or MudPopover

**Layer 3: Omnisearch (deliberate search)**
- User actively seeks information → Ctrl+K → type query → find it

**Layer 4: Help page (full reference)**
- A dedicated `/help` route that shows:
  - Categorized command reference (pulled from help entries)
  - "How to use this site" guide (a wiki page, editable)
  - Link to the full wiki
  - FAQ section (a wiki category)

### First-Visit Experience

When a player creates an account (or first logs in), they see:

```
┌─────────────────────────────────────────────────────────┐
│  👋 Welcome to <Game Name>!                               │
│                                                           │
│  Here's how to get started:                              │
│                                                           │
│  1. 📖 Read the basics         → Getting Started wiki    │
│  2. 👤 Set up your character   → Character Creation wiki │
│  3. 🎭 Join a scene            → Active Scenes page      │
│  4. 💬 Open the terminal       → Connect & play          │
│                                                           │
│  💡 Tip: Press Ctrl+K anytime to search for anything     │
│                                                           │
│  [Got it, don't show again]   [Show me around]           │
└─────────────────────────────────────────────────────────┘
```

- "Show me around" triggers a lightweight guided tour (step-through highlights
  of the left nav, terminal toggle, omnisearch, and profile link)
- Tour state stored per-account — only shown once unless reset in settings
- The four links point to wiki pages — admin controls what new players see
  by editing those wiki slugs

### Empty State Design

Every page must have a meaningful empty state:

| Page       | Empty state message                                      |
|------------|----------------------------------------------------------|
| Scenes     | "No active scenes right now. [Start one?]"              |
| Wiki       | "The wiki is empty. [Create the first page?]" (if admin)|
| Mail       | "Your inbox is empty. You're all caught up! ✓"          |
| Characters | "No characters found. [Create your first character?]"   |
| Events     | "No upcoming events. [Create one?]" (if authorized)     |
| Search     | "No results for 'X'. Try: [Wiki] [Characters] [Help]"  |

Empty states are NOT blank — they're opportunities to guide the user.

---

## Navigation Architecture

### TopBar (always visible)

```
[Logo/Name]  [Primary Nav Links...]  [Omnisearch]  [User Actions]
```

- **Logo/Name**: clicks to home. Admin-configurable via branding widget.
- **Primary Nav Links**: the most-used pages. Admin picks which links appear
  here (from the layout config's `navLinks` with `slot: "topbar"`).
  Default: Wiki, Scenes, Characters.
- **Omnisearch**: always visible in the topbar as a compact input that expands
  on focus. Shows "Search... (Ctrl+K)" as placeholder.
- **User Actions** (authenticated): Mail badge, notification bell, avatar dropdown.
- **User Actions** (visitor): [Login] [Register] buttons.

### Left Sidebar (authenticated users)

Full navigation tree. Admin-configured links with icons. Collapsible.

Default structure:
```
• Home              (/)
• Wiki              (/wiki)
• Characters        (/characters)
• Scenes            (/scenes)
• Forums            (/bbs)         [if enabled]
• Events            (/events)      [if enabled]
• Mail              (/mail)        [badge: unread count]
───────────────────
• Play (Terminal)   (/play)
───────────────────
• Settings          (/settings)
• Admin             (/admin)       [if admin]
```

Admin can:
- Add/remove/reorder links
- Add custom links (external URLs, specific wiki pages)
- Add dividers between groups
- Set role restrictions on individual links (admin-only, builder-only)
- Add icon + badge expressions (e.g., mail shows unread count)

### Breadcrumbs (contextual)

Every page deeper than top-level shows breadcrumbs:

```
Home > Wiki > Rules > Combat
Home > Characters > Aldric the Wanderer
Home > Scenes > The Market Square
```

For wiki, breadcrumbs derive from the slug hierarchy:
- `rules/combat/melee` → Home > Wiki > Rules > Combat > Melee

### Mobile Responsiveness

- TopBar collapses: logo + hamburger menu + omnisearch icon
- Left sidebar becomes a slide-out drawer (hamburger toggle)
- Terminal becomes full-screen on mobile (swipe up from bottom)
- Cards stack vertically on narrow screens
- Omnisearch becomes full-screen overlay on mobile

---

## Home Page as Widget Composition

The home page is NOT a hardcoded layout. It's a **widget page** — a sequence of
widgets that the admin arranges. The defaults described above are just the
out-of-the-box configuration.

### Default Home Page Widget Stack

```json
{
  "route": "/",
  "widgets": [
    { "id": "hero-banner", "config": { "showCta": true } },
    {
      "id": "two-column",
      "config": {
        "left": { "id": "getting-started-links" },
        "right": { "id": "game-at-a-glance" }
      }
    },
    { "id": "recent-activity", "config": { "limit": 5, "publicOnly": true } },
    { "id": "wiki-content-block", "config": { "slug": "home" } }
  ]
}
```

### Available Home Page Widgets

| Widget ID              | What it shows                                    |
|------------------------|--------------------------------------------------|
| `hero-banner`          | Header image + game name + tagline + CTA buttons |
| `getting-started-links`| Configurable list of wiki page links             |
| `game-at-a-glance`    | Online count, active scenes, wiki size, last activity |
| `recent-activity`     | Chronological feed of public game events         |
| `wiki-content-block`  | Renders a wiki page inline (configurable slug)   |
| `online-players`      | Who's connected right now                        |
| `upcoming-events`     | Next N events from the calendar                  |
| `custom-html`         | Admin-authored HTML/Markdown block               |
| `featured-characters` | Gallery of spotlighted characters                |
| `scene-spotlight`     | Currently running scenes with participant count  |

### Authenticated vs. Visitor

The admin configures TWO home page layouts:
- `home_visitor` — what unauthenticated visitors see
- `home_player` — what authenticated players see

This is a single toggle in the layout editor:
"Show different home page for logged-in users? [Yes/No]"

If "No", everyone sees the same layout (visitor version).

---

## Configuration Surface

### Admin Settings (DB-backed, editable from admin panel)

```csharp
public record PortalHomeConfig
{
    /// Wiki page slug to render as the main "welcome" block.
    /// Set to null to hide the wiki content block.
    public string? WelcomeWikiSlug { get; init; } = "home";
    
    /// Wiki slugs shown in the "Getting Started" card.
    /// Order matters — displayed as a numbered list.
    public string[] GettingStartedSlugs { get; init; } = [
        "getting-started",
        "how-to-connect",
        "character-creation",
        "rules"
    ];
    
    /// Whether to show the hero banner on the home page.
    public bool ShowHeroBanner { get; init; } = true;
    
    /// Whether to show "Game at a Glance" stats card.
    public bool ShowGameStats { get; init; } = true;
    
    /// Number of recent activity items to show.
    public int RecentActivityLimit { get; init; } = 5;
    
    /// Which activity types to show publicly.
    public ActivityType[] PublicActivityTypes { get; init; } = [
        ActivityType.ScenePosted,
        ActivityType.WikiUpdated,
        ActivityType.CharacterApproved,
        ActivityType.EventCreated
    ];
    
    /// First-visit onboarding: which wiki slugs to show in the welcome modal.
    public string[] OnboardingSlugs { get; init; } = [
        "getting-started",
        "character-creation"
    ];
    
    /// Whether to enable the guided tour for new accounts.
    public bool EnableGuidedTour { get; init; } = true;
}
```

### Relationship to Layout System

The home page config lives alongside (not inside) the layout JSON. The layout
system controls WHERE slots are (topbar, sidebars, bottom bar). The home page
config controls WHAT widgets fill the main content area on the `/` route.

Other routes (wiki, scenes, characters) have their own page-level components
and don't use the widget-stack model — they're standard routed Blazor pages.

---

## Comparison: SharpMUSH vs. AresMUSH Portal

| Feature                  | AresMUSH                      | SharpMUSH                    |
|--------------------------|-------------------------------|------------------------------|
| Home page content        | Static Markdown text          | Widget-composed, wiki-backed |
| Search                   | Algolia (external service)    | Built-in federated omnisearch|
| Search scope             | Wiki only                     | Wiki + chars + scenes + help |
| Navigation               | YAML-configured navbar        | Drag-and-drop layout editor  |
| Theming                  | SCSS color vars + custom CSS  | Live theme editor + presets  |
| Player color prefs       | No                            | Yes (palette overrides)      |
| Getting started          | Manual wiki page link         | Structured onboarding flow   |
| Empty states             | Blank / generic               | Contextual with CTAs         |
| Mobile                   | Responsive (Bootstrap)        | Responsive (MudBlazor)       |
| Keyboard navigation      | No                            | Ctrl+K omnisearch, shortcuts |
| Help discoverability     | Wiki search + in-game help    | 4-layer system (ambient → explicit) |
| First-visit experience   | None                          | Welcome modal + guided tour  |
| Real-time on home page   | No                            | Live stats via SignalR       |
| Wiki on home page        | Static render                 | Live wiki block (auto-updates)|
