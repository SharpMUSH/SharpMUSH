# Admin Panel

## Overview

Blazor panel at `/admin`. Role-gated: Royalty sees player management and
moderation. Wizard sees everything. God sees server-level settings. Web-native
operations only — no duplication of in-game @-commands.

## Access Control

```
Royalty+  → /admin (dashboard, players, moderation)
Wizard+   → /admin (+ config, layout, wiki admin)
God       → /admin (+ server settings)
```

If a Player navigates to `/admin`, they get a 403 page. The nav link to Admin
is not rendered for roles below Royalty.

## Sections

### Dashboard (`/admin`)

Overview cards:
- Online players (count + trend)
- Active scenes (count)
- New registrations (last 7 days)
- Pending reports (count, red if > 0)
- Recent audit log entries (last 10)

### Player Management (`/admin/players`)

**List view:**
- Table: account name, linked characters, last login, role, status (active/banned)
- Search/filter by name, role, status
- Click row → detail view

**Detail view (`/admin/players/{id}`):**
- Account info: email, created date, last login, IP (God only)
- Linked characters: name, dbref, flags, last activity
- Actions: ban/unban account, force password reset, unlink character
- Character detail: flags summary, attribute count, mail count
- No attribute editor — that's in-game `@set` territory

### Moderation (`/admin/moderation`)

**Reports queue:**
- Reported wiki pages, scene poses, profile content
- Each report: reporter, target content, reason, timestamp
- Actions: dismiss report, delete content, warn player, ban player

**Bans:**
- Active bans list (account bans, IP bans if implemented)
- Add/remove bans
- Ban reason + expiry (permanent or timed)

**Audit Log (`/admin/moderation/audit`):**
- Searchable log of all staff actions
- Fields: who, what, target, timestamp, details
- Filter by action type, staff member, date range
- Logged actions: bans, unbans, role changes, page deletions, forced scene
  ends, config changes, layout changes

### Site Configuration (`/admin/config`)

Form-based UI (not raw text editing). Sections:

**General:**
- Site name
- Site description (short, used in OpenGraph)
- Front page welcome text (Markdown editor with preview)
- Registration: open / closed / invite-only

**Limits:**
- Max characters per account (default: 5)
- Max temp rooms per player (default: 3)
- Mail body max length (default: 10000)
- Mail max recipients (default: 10)
- Wiki page max size (default: 50000 chars)
- Image upload max size (default: 5MB)

**Features:**
- Toggle: wiki enabled
- Toggle: scenes enabled
- Toggle: mail enabled
- Toggle: public profiles (or login-required for all)
- Toggle: guest access to public content

Changes write to the game's configuration store. Hot-reload where the engine
supports it; otherwise note "requires restart" next to the field.

### Layout Editor (`/admin/layout`)

See Widget System doc for details. Lives at `/admin/layout`.

- Visual zone editor (TopBar, Left, Right, Main, Footer)
- Drag widgets between zones
- Configure per-widget settings (Quick Links targets, Welcome Text content)
- Reorder widgets within a zone
- Preview before publish
- Save → writes layout JSON to config store → event invalidates cached layout

### Wiki Admin (`/admin/wiki`)

- List all pages (sortable by name, last edit, protection status)
- Bulk operations: protect, unprotect, delete
- Orphaned pages (no incoming links)
- Most-edited pages
- Page lock/protection management

### Server Settings (`/admin/server`) — God Only

- View server version, uptime, connected players
- NATS connection status
- Database connection status
- Cache statistics (hit/miss ratio)
- Restart/shutdown controls (if exposed by engine)
- Raw config viewer (read-only for diagnostics)

## Design Principles

1. **No command duplication.** If `@set` does it in-game, don't build a web
   form for it. Admin panel covers web-native needs (layout, moderation,
   account management).

2. **Audit everything.** Every staff action in the admin panel is logged with
   who, what, when. No silent changes.

3. **Progressive disclosure.** Dashboard shows counts and alerts. Detail views
   are one click away. Don't overwhelm with data upfront.

4. **Confirmation for destructive actions.** Delete, ban, force-end — all
   require a confirmation dialog with the specific action described.

5. **Responsive.** Admin panel works on tablet (staff on mobile occasionally).
   Not optimized for phone — that's acceptable for admin work.
