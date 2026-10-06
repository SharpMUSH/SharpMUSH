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
- Recent audit log entries (last 10)

### Player Management (`/admin/accounts`, `/admin/characters`)

**List view:**
- Table: account name, linked characters, last login, role, status (active/banned)
- Search/filter by name, role, status
- Click row → detail view

**Detail view (`/admin/characters/{id}`):**
- Account info: email, created date, last login, IP (God only)
- Linked characters: name, dbref, flags, last activity
- Actions: ban/unban account, force password reset, unlink character
- Character detail: flags summary, attribute count, mail count
- No attribute editor — that's in-game `@set` territory

### Moderation (`/admin/moderation`)

No report queue (#1565): games take reports their own way (softcode, +jobs, Discord), so the panel
standardizes only the actions staff take. Content removal stays where the content lives (wiki delete,
scene pose delete, profile edit).

**Actions:**
- Pick a character, give a reason, then warn, boot, ban the account, or unlink the character
- Warn fires the game's `PLAYER`WARN` event (staff character as `%#`; objid, reason, staff account
  name as `%0`-`%2`); the game's handler decides what a warning does
- Every action is audited with its reason

**Bans:**
- Account bans: reason, staff member, expiry (permanent or timed); a timed ban lifts by itself
- Signing in as a banned account (portal, telnet `login`, telnet `connect`, MCP) is refused with
  the expiry
- Host bans stay in the sitelock (`config.admin`); the page lists its rules read-only

**Audit Log (`/admin/audit`):**
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

Changes write to the game's configuration store and take effect without a restart.
The listener options a `mush.cnf` carries that nothing reads (`portal_port`,
`ssl_portal_port`, `ip_addr`, `ssl_ip_addr`, `socket_file`, `use_ws`, `ws_url`) are
marked "Not used", each with what sets it instead (the ConnectionServer settings, or
the server's Kestrel addresses), via `SharpConfigAttribute.Unused`. `port` and
`ssl_port` open no listener either, but MSSP reports them, so their
descriptions say so instead. No other option
needs a restart, so there is no restart marker (#1565).

### MSSP (`/admin/config/mssp`) — `config.admin`

What crawlers read through the MSSP telnet option and `MSSP-REQUEST`, one report for
both (`IMsspReportService`). Rows follow the specification's tables
(`MsspCatalog`, in `SharpMUSH.Configuration`). A variable the server reports itself
(name, players, uptime, ports, website, codebase, family, charsets, protocols) is a greyed,
read-only row naming where it comes from, with a link to the section that sets it. The rest
edit the `mssp` option (`MsspOptions`, a variable → values dictionary in the configuration
store, so it needs no schema of its own). Names MSSP does not list go under "Other
variables". A `mush.cnf` import reads PennMUSH's `mssp name/value` lines into it. The
connection server holds the latest report, which the main process sends when it changes
(`MsspReportPublisher`) and when a connection server starts and asks for it.

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

### Server (`/admin/server`) — `server.admin`

Stat tiles read in-process from `GET api/admin/server/status`, refreshed every
10 seconds while the page is open:

- Readiness (`ServerReadiness`, the same answer as `/ready`) and what it waits on
- Uptime, version and portal build
- Players online and open connections
- Queued jobs against `global_queue_limit` (the count `@ps/all` reports)
- Message bus backlog and the fullest stream against its byte budget (`NatsMessagingMetrics`)
- World data, file and map size, and disk free (`IStorageCapacityService`, as `@storage`)
- Last backup (`IWorldBackupService`)

No restart or shutdown controls: `@shutdown` (and `@shutdown/reboot`) covers them in-game.

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
