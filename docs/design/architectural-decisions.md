# Architectural Decisions Record

Resolved design decisions for the SharpMUSH web portal. Each entry documents
the chosen approach, rationale, and constraints. Reference these when
implementing — they are binding unless explicitly superseded by a later decision.

---

## 1. Authentication & Sessions

### 1.1 Identity Provider: ASP.NET Identity + Game Character Layer

**Decision:** ASP.NET Identity manages web accounts (email, password hash,
external logins). Game characters retain their own passwords in the SharpMUSH
object DB. The two layers are linked by a foreign key (AccountId on character).

**Trust model:** Account login = full access to all linked characters. No
character password required after account authentication.

**Existing implementation** (already built):
- MUSH: `login <name-or-email> <password>` → AccountMode
- MUSH: `play <character-name>` → connects as character (no char password)
- Web: `POST /api/auth/account-login` → session token + character list
- Web: `POST /api/auth/mush-token` with AccountSessionToken → OTT (no char password)
- Legacy: `connect <character> <password>` → direct login (unlinked characters)

**Character password purpose (limited scope):**
- Legacy `connect` command (for unlinked characters or traditional MU* clients)
- Claiming/linking a character to an account (proving ownership)
- Initial creation via `make` (sets a character password for legacy compat)

**Migration path for existing games:**
- First web login for legacy players: "Claim" flow — enter character name +
  character password → system creates a web account and links the character
- Admin bulk-import option for migrating existing game databases

### 1.2 Token Strategy: JWT in Memory + httpOnly Refresh Cookie

**Decision:** Short-lived JWT (15 min) held in WASM memory (C# static service,
not localStorage). httpOnly secure refresh cookie enables silent renewal.

**Flow:**
1. Account login → server returns JWT + sets httpOnly refresh cookie
2. WASM stores JWT in `AccountAuthService` (memory-only, dies on tab close)
3. On JWT expiry → silent refresh via cookie (no user interaction)
4. Tab close → JWT gone, refresh cookie remains → next visit auto-refreshes

**Existing implementation** (partially built):
- `IAccountSessionStore` already manages session tokens with sliding TTL (15min)
- `AccountAuthService` on client handles login/token storage
- Needs: httpOnly cookie mechanism (currently uses in-memory session token)

### 1.3 Account ↔ Character: Tab = Character, Characterless Mode

**Decision:** Each browser tab represents one character (or no character).
Character is selected after account login. No hot-swapping within a tab.

**Characterless mode:** Accounts with 0 characters enter the portal in a
limited mode. They can browse wiki, view profiles, read public content, and
create a character. They cannot enter scenes or send mail.

**Flow:**
1. Login to account (any method)
2. Character picker shown (or "Create your first character" if 0 chars)
3. Select character → tab is bound to that character
4. Opening another character → "Open in new tab" (new WASM instance)

**Existing implementation:** `ConnectionService.BindAccount` handles AccountMode
(pre-character-selection state). `ConnectionService.Bind` transitions to
LoggedIn (character-bound state).

### 1.4 Permissions: Hierarchical Roles (Expandable Later)

**Decision:** Start with PennMUSH-style hierarchy for familiarity:

| Level | Role         | Description                                    |
|-------|--------------|------------------------------------------------|
| 0     | Guest        | Visitor / unauthenticated browser              |
| 1     | Player       | Authenticated, can play, pose, mail, wiki edit |
| 2     | Royalty      | Can moderate, approve characters               |
| 3     | Wizard       | Full admin, can build, @halt, manage objects   |
| 4     | God (#1)     | Root access, owns the server                   |

**Future expansion:** The admin panel will support custom permission scopes
(e.g. "Wiki Moderator" = Player + wiki.delete). The hierarchical model is the
STARTING POINT, not the ceiling. Under the hood, implement as RBAC where each
tier is a role bundling atomic permissions. This allows custom roles later
without redesigning the system.

**Web portal scope mapping:**
- Guest: browse wiki, view public profiles, read scene archives
- Player: all Guest + pose in scenes, send mail, edit wiki, manage own characters
- Royalty: all Player + approve characters, moderate forums, lock wiki pages
- Wizard: all Royalty + admin panel access, layout editing, theme editing
- God: all Wizard + server config, account management, permission assignment

### 1.5 Login Methods: Password + Discord OAuth (Progressive)

**Decision:** Ship with email+password (ASP.NET Identity). Add Discord OAuth
as the first external provider. Google/GitHub as later additions.

**Why Discord first:** The MU* community organizes on Discord. "Log in with
Discord" is the highest-ROI external auth integration.

**Important:** External auth (Discord, Google) authenticates the WEB ACCOUNT
only. Once authenticated, character access follows the same trust model — no
character password needed for linked characters.

**Progressive plan:**
1. Phase 1: Email + password (ASP.NET Identity)
2. Phase 2: Discord OAuth ("Link Discord" in account settings)
3. Phase 3: Google OAuth (if demand exists)
4. Future: Passkeys/WebAuthn as opt-in

---

## 2. Real-Time Architecture

### 2.1 Client Transport: SignalR (JSON)

**Decision:** Clients connect via SignalR with JSON serialization. The server
manages all NATS subscriptions internally — clients never know about NATS.

**Rationale:** SignalR gives us automatic reconnection, transport fallback
(WebSocket → SSE → LongPolling), group management, and first-class Blazor
support. JSON is debuggable (readable in network tab). MessagePack can be
swapped in later as a one-line optimization if needed.

### 2.2 Server Fan-Out: Broad Subjects + Schema Filtering + JetStream

**Decision:** Use a small number of NATS subjects (6-8 total) with message
schemas carrying routing metadata. Server-side code filters and fans out to
appropriate SignalR groups.

**Subject layout:**

```
Core NATS (ephemeral, pub/sub):
  portal.presence         — all presence state changes
  portal.scene.live       — all live scene events
  portal.page.viewers     — all co-viewing events

JetStream (durable streams):
  SCENES stream → portal.scene.log      — archived scene logs
  MAIL stream   → portal.mail           — mail delivery
  NOTIFY stream → portal.notify         — notification events
  WIKI stream   → portal.wiki.changes   — wiki edit events
```

**Message envelope:**
```json
{
  "type": "pose",
  "scene_id": "scene-42",
  "character_id": "char-7",
  "timestamp": "2025-06-05T14:30:00Z",
  "payload": { ... }
}
```

**Fan-out logic:** Server subscribes to broad subjects. On message receipt,
checks which SignalR groups (mapped to scenes/characters) need it, sends only
to matching groups. Subscribe to a NATS subject when at least one client needs
it; unsubscribe when the last client disconnects from that subject's content.

**JetStream stream configuration:**
- SCENES: retention = 30 days, max 100GB
- MAIL: retention = unlimited (until user deletes)
- NOTIFY: retention = 7 days
- WIKI: retention = unlimited (audit trail)

### 2.3 Subject Namespace: Hierarchical, Schema-Discriminated

**Decision:** Hierarchical namespace (`portal.scene.live`) but few subjects.
Routing happens via message schema fields (scene_id, character_id), not via
subject-per-entity.

**Rationale:** Thousands of subjects (one per scene, one per character) creates
operational headaches — monitoring, debugging, JetStream consumer configuration.
Broad subjects with schema filtering is operationally simpler and NATS handles
the throughput easily for any realistic MU* population.

### 2.4 Reconnection: Hybrid Snapshot + Bounded Replay

**Decision:** Short disconnects (<5 minutes) get event replay from JetStream
sequence numbers. Long disconnects (≥5 minutes) get a fresh state snapshot
via REST API.

**Threshold:** 5 minutes (configurable via `Portal:ReconnectReplayThreshold`
in appsettings).

**Flow:**
- Client tracks last-seen JetStream sequence number per stream (IndexedDB)
- On reconnect: if disconnected <5min → request replay from sequence number
- On reconnect: if disconnected ≥5min → call REST endpoints to load fresh state
- Threshold check: compare disconnect timestamp (stored client-side) with now

### 2.5 Rate Limiting: Client Throttle + Server Hard Limit

**Decision:** Client-side debounce for smooth UX. Server-side hard limits as
security backstop.

**Limits:**
- Scene poses: 10/second per character (human typing speed is ~1-2/second)
- Presence updates: 1/15 seconds per character
- Wiki edits: 1/second per account (prevent rapid-fire saves)
- Mail sends: 5/minute per character

**Client-side:** Debounce rapid input (e.g. batch command sequences). Disable
send button briefly after pose submission (100ms cooldown, invisible to user).

**Server-side:** Hard reject with 429-equivalent message if limits exceeded.
NATS may provide natural backpressure via JetStream consumer ack timing.

---

## 3. Component & State Architecture

### 3.1 State Management: Injectable Services (DI-Based)

**Decision:** Scoped services per-circuit for all state management. No external
state library (no Fluxor, no Redux pattern).

**Pattern:**
- `SceneStateService`, `PresenceService`, `NotificationService`, etc.
- Components inject services, call methods, subscribe to `OnChange` events
- Services call `StateHasChanged()` via event callbacks

**Rationale:** Idiomatic Blazor, minimal ceremony, no external dependencies,
easy for contributors to understand. The MU* portal's state, while real-time,
is manageable without a formal state machine framework — SignalR events map
naturally to service method calls.

### 3.2 Rendering Mode: InteractiveAuto (SSR → WASM Handoff)

**Decision:** Use .NET 8+ InteractiveAuto rendering mode. Server-side renders
the first paint (instant HTML for crawlers and fast first load), then WASM
downloads in background and takes over interactivity.

**Benefits:**
- SEO: wiki pages are crawlable (server-rendered HTML)
- Link previews: Discord/Slack embeds show page content (OG tags from SSR)
- Fast first paint: visitors see content before WASM downloads
- Full interactivity: once WASM loads, the experience is client-side

**Constraints:**
- Components must work in both server and client render contexts
- State must survive the server → client handoff
- No direct DOM access during server-side prerender

### 3.3 Module Structure: Main App + Lazy-Loaded Admin

**Decision:** Two main assemblies plus a shared project:

```
SharpMUSH.Client.Shared    — DTOs, interfaces, shared components, services
SharpMUSH.Client           — Main app: layout, all player-facing pages
SharpMUSH.Client.Admin     — Admin panel (lazy-loaded on /admin routes)
```

**Rationale:** Wiki, scenes, profiles, mail — these are all core player features
used every session. Splitting them into separate lazy-loaded assemblies adds
navigation latency for no benefit (players use all of them). The admin panel,
however, is only accessed by staff (5% of users) — lazy loading it avoids
shipping admin code to everyone.

### 3.4 Error Boundaries: Layered (Per-Widget + Global Fallback)

**Decision:** Per-widget error boundaries for fault isolation. Global boundary
as ultimate fallback.

**Layout:**
- Each sidebar widget: own `<ErrorBoundary>`
- Terminal panel: own `<ErrorBoundary>`
- Notification center: own `<ErrorBoundary>`
- Main content area: own `<ErrorBoundary>`
- Global (MainLayout): catches anything that escapes inner boundaries

**Critical rule:** A crash in any non-essential component (sidebar, notifications,
presence) MUST NOT disrupt the terminal or active scene. Players in the middle
of writing a pose must never lose text due to an unrelated component failure.

---

## 4. API Contract Design

### 4.1 Communication: REST for Reads, SignalR for Writes + Push

**Decision:**
- Read operations: REST (`GET /api/wiki/{slug}`, `GET /api/characters/{name}`)
- Write operations: SignalR hub methods (`PostPose`, `SendMail`, `UpdateWikiPage`)
- Real-time push: SignalR callbacks (`OnPoseReceived`, `OnPresenceChanged`)

**Rationale:**
- REST reads are cacheable (browser cache, service worker, CDN for static pages)
- REST reads have Swagger/OpenAPI documentation (debuggable with curl)
- SignalR writes get immediate broadcast to other clients (no poll needed)
- SignalR push is the natural channel for real-time events

### 4.2 Pagination: Cursor for Real-Time, Offset for Stable

**Decision:** Pagination strategy varies by content type:

| Content          | Strategy      | Reason                              |
|------------------|---------------|-------------------------------------|
| Scene logs       | Cursor-based  | Time-ordered, concurrent writes     |
| Notifications    | Cursor-based  | Time-ordered stream                 |
| Search results   | Cursor-based  | Results may change between requests |
| Mail inbox       | Cursor-based  | New mail arrives continuously       |
| Wiki page list   | Offset-based  | Stable, admin-curated, browseable   |
| Character list   | Offset-based  | Stable, infrequently changing       |
| Scene archive    | Offset-based  | Historical, immutable               |

### 4.3 Shared Types: Shared DTO Project

**Decision:** Single `SharpMUSH.Client.Shared` assembly with all request/response
types referenced by both server and client.

**Contents:**
- Request/response DTOs (e.g. `WikiPageDto`, `PoseDto`, `CharacterSummaryDto`)
- Service interfaces (`IWikiService`, `ISceneService`)
- SignalR hub interface contracts (shared between server hub and client proxy)
- Enums, constants, shared validation logic

### 4.4 File Storage: Local Filesystem Behind IFileStorage Interface

**Decision:** Files stored on local filesystem. Abstracted behind `IFileStorage`
interface for future migration to MinIO/S3 if needed.

**Directory structure:**
```
/var/sharpmush/uploads/
  portraits/{character_id}/avatar.webp
  wiki/{page_slug}/{filename}
  attachments/{id}/{filename}
```

**Interface:**
```csharp
public interface IFileStorage
{
    Task<string> SaveAsync(Stream content, string path, CancellationToken ct);
    Task<Stream> GetAsync(string path, CancellationToken ct);
    Task DeleteAsync(string path, CancellationToken ct);
    string GetPublicUrl(string path);
}
```

**Default implementation:** `LocalFileStorage` — writes to configured directory,
serves via ASP.NET static files or a file controller.

**Migration path:** Swap `LocalFileStorage` for `MinioFileStorage` when scaling
requires it. Client code unchanged (URLs work either way).

---

## 5. Content Formatting & Rendering Pipeline

### 5.1 Markdown Processor: Markdig (Same Library, Both Sides)

**Decision:** Markdig runs in both ASP.NET server and Blazor WASM. Same library,
same extensions, same output. No rendering discrepancy between preview and save.

**Custom extensions (Markdig pipeline):**
- `[[wiki-links]]` → resolved at render time to `<a href="/wiki/{slug}">`
- `@character-mentions` → resolved to character profile links
- `#scene-links` → resolved to scene archive links
- Standard GFM extensions (tables, task lists, fenced code blocks)

**Rationale:** Markdig is already used for MUSH wiki rendering. Using it in
WASM guarantees "preview = final output." The ~150KB bundle cost is paid once
and cached.

### 5.2 Wiki Link Resolution: Lazy (Resolve at Render Time)

**Decision:** `[[Combat Rules]]` stored as-is in Markdown source. At render
time, look up slug → render link. Non-existent pages render as red/dashed
"create this page" links.

**Caching:** Maintain an in-memory dictionary of `slug → exists?` (invalidated
via NATS `portal.wiki.changes` events). Lookup is a dictionary hit, not a DB
query per render.

**Renames:** Renaming a page updates only the page's own slug. All inbound links
automatically resolve to the new URL because they reference by title, which the
page still matches (or a redirect from old slug → new slug).

### 5.3 MString/ANSI → HTML: Client-Side Conversion

**Decision:** Server sends MString/ANSI representation over SignalR. Client-side
C# code in WASM converts to styled HTML for display in the terminal component.

**Benefits:**
- Smaller wire messages (ANSI codes are compact vs. HTML spans)
- Client can re-render with different styles (user changes font/color preference)
- MString library is C# — runs in WASM without modification
- Conversion cost is negligible for text content

### 5.4 Sanitization: Markdown-Only, No Raw HTML

**Decision:** User-generated content is Markdown-only. Raw HTML tags in source
are escaped (rendered as literal text, not interpreted as HTML).

**Markdig configuration:** Disable `HtmlInline` and `HtmlBlock` extensions.
Any `<tag>` in user input becomes `&lt;tag&gt;` in output.

**Admin exception:** A per-page flag `AllowHtml` (settable by Wizard+ only)
enables raw HTML on specific pages (game home page, custom landing pages).
This flag is NOT available to Players or Royalty.

**Result:** XSS is structurally impossible for 99% of content. The attack
surface is limited to admin-authored HTML pages (which are trusted by definition).

---

## Design Documents Index

| Document                          | Status    | Covers                              |
|-----------------------------------|-----------|-------------------------------------|
| web-portal-vision.md              | Complete  | Architecture, layout, widgets, phases|
| wiki-shared-content.md            | Complete  | Wiki schema, dual-render, @wiki     |
| front-page-and-navigation.md      | Complete  | Front page, omnisearch, help, onboard|
| ui-patterns.md                    | Complete  | 17 UX patterns + anti-patterns      |
| architectural-decisions.md        | Complete  | This document                       |
| scene-system.md                   | Pending   | Scene lifecycle, UI, real-time       |
| character-profiles.md             | Pending   | Profile pages, sheets, gallery       |
| content-rendering-pipeline.md     | Pending   | Full rendering pipeline details      |
| mail-messaging.md                 | Pending   | In-game mail on web                  |
| url-strategy.md                   | Pending   | Routes, deep links, SEO              |
