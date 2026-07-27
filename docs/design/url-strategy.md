# URL Strategy

## Overview

Clean, stable, direct-linkable URLs. No hash routing. Blazor WASM handles
all routes client-side after initial load. Server returns index.html for all
non-API routes (standard WASM hosting pattern).

## Route Map

### Public Content

```
/                           Front page (widgets, welcome)
/wiki                       Wiki index / recent changes
/wiki/Page_Name             Wiki page (underscores, case-insensitive lookup)
/wiki/Page_Name/edit        Wiki page editor
/wiki/Page_Name/history     Page revision history
/character/CharacterName    Character profile (cleaner than /wiki/Character:X)
/characters                 Character directory (public profiles)
/scenes                     Scene archive (completed, public)
/scenes/42                  Scene archive detail (numeric ID permalink)
/scenes/active              Active scenes list
/help                       Help file index
/help/Topic_Name            Help file page
/login                      Login page
/register                   Registration page
```

### Authenticated Content

```
/mail                       Mail inbox (current character)
/mail/42                    Mail message detail
/mail/compose               Compose new mail
/play                       Game terminal (current character)
/scenes/42/live             Live scene participation
/apps/{slug}                Dynamic Application (schema-driven; role-gated per registry)
/settings                   Account settings
/settings/characters        Character management
/settings/theme             Color/theme preference
```

### Admin Panel (Wizard+)

```
/admin                      Admin dashboard
/admin/players              Player/account list
/admin/players/42           Player detail
/admin/characters           Character list
/admin/config               Site configuration
/admin/layout               Layout/widget editor
/admin/moderation           Reports, bans, audit log
/admin/wiki                 Wiki admin (protected pages, bulk ops)
/admin/server               Server settings (God only)
```

### API Routes (Not Client-Routed)

```
/api/...                    REST endpoints (reads)
/api/applications           Dynamic Application registry (admin, Wizard+)
/hubs/game                  SignalR hub (WebSocket)
/http/...                   HTTP handler (game engine bridge; runs <METHOD> softcode)
```

## URL Conventions

### Wiki Pages

- `/wiki/{namespace}/{category}/{slug}` — the canonical page route. All three
  segments are part of a page's identity.
- Underscores replace spaces in the slug; lookup is case-insensitive
- Display always shows the page's canonical title (original case)
- Special characters in slugs are percent-encoded
- `/help/{topic}` and `/character/{name}` are aliases resolving to the `help`
  and `character` namespaces at the default `general` category

### Locale

`?lang=<BCP-47 tag>` is the **only** locale mechanism for wiki content. There is
no locale path prefix and no locale-suffixed slug.

- One canonical slug per page, whatever locale is being read. `?lang=` selects a
  view of that page; it never creates a second page, and it never changes the
  `<link rel="canonical">` the prerender emits.
- A malformed or unknown tag is treated as absent and falls back to the
  configured `Wiki.DefaultLocale`. It is never a 400 — a read cannot fail for
  locale reasons.
- Absent `?lang=`, the portal sends the locale from the `locale` localStorage key
  the language picker writes. An explicit `?lang=` wins over that preference.
- `?lang=` is accepted on the page read, the listing endpoints
  (`recent`, `ns/{ns}`, `pages`, `category/{c}`, `tag/{t}`) and
  `{slug}/revisions`. Listings return localized titles and still return **one row
  per page**.
- `[[WikiLink]]` targets and the unique slug index are unaffected: neither has a
  locale dimension.

Because `?lang=` does not change page identity, it also does not change any
permalink: a link someone copies out of the address bar keeps working when the
reader's locale differs.

### Character Biographies

A character's biography is the Character-namespace page whose slug is the
character name, and `/character/{name}` is its **only** canonical URL. The
wiki route it is stored under is an implementation detail:

- Nothing in the portal links to `/wiki/character/general/{slug}`. Every link
  producer — the wiki index, recent-changes, directive blocks, the wiki admin
  grid, `[[wiki links]]` in markup, and the sitemap — goes through
  `WikiRoutes.PathFor`, which returns the alias for these pages.
- `CanonicalUrlMiddleware` 301s the wiki route to the alias, catching
  bookmarks, external links, and markup rendered before that change.
- `WikiPage.razor` performs the same redirect for client-side navigations,
  which never reach the server. It replaces the history entry rather than
  pushing one, so Back does not land on a URL that immediately bounces.

Two deliberate limits on the alias:

- **View route only.** `/history`, `/diff` and `/edit` have no equivalent under
  `/character` and keep working where they are. This is also what stops the
  profile page's own history link from bouncing.
- **Default category only.** `/character/{name}` carries no category segment,
  so a Character-namespace page filed under any other category cannot round-trip
  through the alias and keeps its wiki path.

### Scene Permalinks

- `/scenes/42` — numeric ID, never changes
- Title is NOT in the URL (titles can change, would break links)
- Active scenes at `/scenes/active` (list, not individual)
- Live participation at `/scenes/42/live` (redirects to login if not authed)

### Query Parameters

```
/wiki?search=dragon         Omnisearch focused on wiki
/characters?search=elf      Character directory filter
/scenes?page=2              Pagination
/admin/players?q=gandalf    Admin search
```

## Deep Linking

Every page is direct-linkable. Sharing `/scenes/42` or `/wiki/Magic_System`
works — the recipient sees the content (respecting permissions). No state is
required beyond the URL.

The Blazor WASM app handles routing client-side. Server configuration:

```
// All non-API, non-static routes fall through to index.html
app.MapFallbackToFile("index.html");
```

## SEO / Pre-rendering

### What Gets Pre-rendered

Public content only:
- Wiki pages (all public pages)
- Character profiles (public fields only)
- Scene archives (public completed scenes)
- Help files
- Front page

### How

Bot detection by user-agent (Googlebot, Bingbot, etc.) or `_escaped_fragment_`
query param. When bot detected:

1. Server renders the page to static HTML (Markdig for wiki, MString.ToHtml()
   for scene poses, structured data for profiles)
2. Includes `<meta>` OpenGraph tags (title, description, image)
3. Returns complete HTML (no JS required to see content)
4. Cached for 1 hour, invalidated on content edit

### OpenGraph Tags

```html
<!-- Wiki page -->
<meta property="og:title" content="Magic System - GameName Wiki" />
<meta property="og:description" content="First 200 chars of page content..." />
<meta property="og:type" content="article" />
<meta property="og:url" content="https://game.example.com/wiki/main/general/magic_system" />

<!-- Character profile -->
<meta property="og:title" content="Gandalf - GameName" />
<meta property="og:description" content="A wandering wizard..." />
<meta property="og:image" content="https://game.example.com/files/gandalf-icon.jpg" />

<!-- Scene archive -->
<meta property="og:title" content="The Council of Elrond - Scene Archive" />
<meta property="og:description" content="4 participants, 47 poses, completed June 2025" />
```

### hreflang

Prerendered wiki pages emit one `<link rel="alternate" hreflang="…">` per locale
the page can actually be read in, plus `hreflang="x-default"` at
`Wiki.DefaultLocale`, and set `<html lang>` to the locale actually served. The
sitemap carries the same alternates as `xhtml:link` entries.

Nothing is emitted for a single-locale page, and unpublished translations are
never advertised — the prerender path resolves with drafts excluded, since it is
unauthenticated by definition.

### Authenticated Content — No SEO

Mail, active scenes, settings, admin panel — none of these are pre-rendered.
Bots get a 403 or a generic "Login required" page. No content leak.

## Canonical URLs

- Each page has exactly one canonical URL
- Redirects for common mistakes:
  - `/wiki/Page Name` (space) → `/wiki/Page_Name` (301 redirect)
  - `/Wiki/Page_Name` (capital W) → `/wiki/Page_Name` (301 redirect)
  - `/character/Name/` (trailing slash) → `/character/Name` (301 redirect)
  - `/wiki/character/general/Name` → `/character/Name` (301 redirect;
    see "Character Biographies" above)
- `<link rel="canonical">` included in pre-rendered pages
- `?lang=` is never canonical. Every locale of a page shares the unsuffixed
  canonical URL, so translations consolidate ranking signals instead of
  competing with each other.
