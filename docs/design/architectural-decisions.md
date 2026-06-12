# Architectural Decisions Record

Resolved design decisions for the SharpMUSH web portal. Each entry documents
the chosen approach, rationale, and constraints. Reference these when
implementing — they are binding unless explicitly superseded by a later decision.

**IMPLEMENTATION GATE:** Before implementing ANY area, review its decisions with
the project owner and confirm agreement. Designs will be refined iteratively —
treat these as the current best understanding, not final specifications. Each
area's TODO list includes a "Review & Confirm Decisions" step as item #1.

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

## 6. Character Profiles

### 6.1 Data Source: Hybrid (HTTP Handler + Portal Caching)

**Decision:** The game's HTTP handler is the sole authority for structured profile
data. The portal caches responses and invalidates on NATS events. Direct DB reads
are not used for profile data.

**Flow:** Portal → HTTP handler → game reads attributes → returns JSON → portal caches.
Invalidation via NATS event when attributes change in-game or via web form POST.

**Bootstrap:** SharpMUSH creates a default HTTP handler object on first run with
stock profile endpoints. Admin customizes via MUSHcode editing.

### 6.2 Schema Ownership: Portal Convention (PROFILE`* attributes)

**Decision:** Game publishes a profile schema via HTTP endpoint. Schema defines
field names, types, visibility rules, editability. Portal renders accordingly.

**Format policy:** Fields are plain text by default (ANSI stripped). Schema can
opt individual fields into `format: "mstring"` (raw ANSI preserved) or
`format: "markdown"` (rendered as Markdown). The HTTP handler decides per-field.

### 6.3 Profile Gallery: Both (Upload + URL)

**Decision:** Players can upload images (stored via IFileStorage) AND reference
external URLs. Gallery metadata lives in portal DB with ordering and captions.
One image can be designated "profile icon" for use in scene panels, character
directory thumbnails, etc.

### 6.4 Profile Identity: Wiki Page in Character: Namespace

**Decision:** A character profile IS a wiki page at `Character:<name>`. The
portal compositor injects the structured section (from HTTP handler) above the
freeform wiki body. This gives profiles full wiki features: revision history,
search indexing, wiki-links, templates, categories.

**Editing:** Structured fields edited via web form (POST to HTTP handler) or
in-game attributes. Freeform content edited via wiki editor. Two edit modes
on one page.

### 6.5 Profile Edit: Web Form → HTTP Handler (with permission schema)

**Decision:** The HTTP handler schema includes `editable_by` and `visible_to`
per field. Portal renders form accordingly. POST to handler validates permissions
server-side. Handler returns only fields the viewer is authorized to see.

---

## 7. Scene System

### 7.1 Scene Tracking: Explicit (Opt-In)

**Decision:** Scenes are explicitly started (`+scene/create`) and ended
(`+scene/end`). No passive room logging. Rooms without an active scene
produce no archived log.

**Rationale:** Privacy by default. Players consent to logging by starting a scene.
OOC conversations in rooms are never recorded unless someone explicitly opts in.

### 7.2 Web Participation: Watch + Direct Scene Pose

**Decision:** Web scene panel is primarily for reading (watching the clean pose
stream). However, the web CAN submit poses directly to a specific scene.

**Key distinction between MUSH and Web pose paths:**

- **MUSH path:** Player types a pose command in their terminal → game processes it
  in their current room → room emit → scene logger captures it (because there's
  an active scene in that room). The pose goes to the ROOM first, scene captures it.

- **Web scene path:** Player submits a pose via the scene panel UI → server routes
  it to the specific scene → scene emits to the scene's room → scene logger records it.
  The pose targets the SCENE first, gets emitted to the room.

Both paths produce the same result (pose appears in room AND in scene log), but
the routing is different. The web path targets a scene_id, not a room.

**Every scene has a room.** Web-created scenes auto-create a temporary room. MUSH
players can `+scene/join <scene#>` to teleport into that temp room and participate
at the same level as web players. No "virtual scene" concept — just rooms that are
temporary vs permanent.

**Temporary room lifecycle:**
- Created on web "New Scene" or `+scene/create/temp`
- No grid exits (access via `+scene/join` only)
- Recycled on scene end (grace period, then @destroy)
- Characters returned to previous location on leave/end

### 7.3 Scene Storage: Separate Collections

**Decision:** Multiple collections: Scenes, Poses, Participants, ActorRoles, Plots.
Not stored as game objects or attributes. Scene data lives in the portal/content DB,
separate from the game object DB.

**Rationale:** Scene data is inherently relational (many poses per scene, many
participants per scene, many scenes per plot). Graph DB collections with indexes
are ideal. Game objects are for game state, not content archives.

### 7.4 Pose Format: MString / ANSI Only

**Decision:** Poses are stored as raw MString (ANSI markup). No Markdown in poses.
Plain text extraction (ANSI-stripped) is stored alongside for search indexing.

**Rendering:**
- In-game: MString rendered natively (ANSI codes interpreted by client)
- Web scene panel: MString → HTML conversion (existing client-side pipeline)
- Scene archive (published log): MString → HTML

**Rationale:** MUSH players format poses with ANSI (bold names, colored speech).
This is the native format. Markdown would be foreign to the MUSH tradition.

### 7.5 Scene Privacy: Granular with Public Default

**Decision:** Scenes can be: public (default), watchers-allowed (can watch but not
search/browse), participants-only, private (hidden from all non-participants).

**Admin default:** `public` out of the box. Any participant can downgrade
visibility (mark private). Only runner can upgrade (make public again after
being marked private by a participant).

**Veto rule:** Any participant can veto publication at any time. Once vetoed,
only that participant can un-veto. This protects player consent.

### 7.6 Scene Discovery: Simple List

**Decision:** Simple list of active scenes. Title, location, participant
count, last activity time. MUSH players can `+scene/join <scene#>` to teleport
into any listed scene (including web-created temp rooms).

**No multi-scene participation:** A character is in one room at a time. Period.
To switch scenes, leave the current one first. This is PennMUSH-native behavior.

**Web "Join" button:** Equivalent to `+scene/join` — teleports character to the
scene's room (temp or grid). MUSH players and web players end up in the same room.

---

## Area 8: Content Rendering Pipeline

### 8.1 MString Is the Shared Library

**Decision:** MString already has `.ToAnsi()`, `.ToHtml()`, `.ToPlainText()`.
Web portal references the same shared library. No separate ANSI-to-HTML parser
needed — MString handles it natively.

### 8.2 Markdown Uses Markdig Everywhere

**Decision:** Single Markdig pipeline configuration used by both server and
client. `DisableHtml()` always on for user content. Custom wiki-link extension
resolves `[[links]]` at render time.

### 8.3 Markdown → MString for In-Game Wiki

**Decision:** Custom Markdig renderer that walks the AST and emits MString
segments. `# Header` → bold + colored MString, `[link]` → MXP clickable, etc.
Same library, different renderer backend.

### 8.4 Poses Are MString Only

**Decision:** Poses are never Markdown. They are MString in, MString out.
Web renders via `.ToHtml()`. No Markdown processing path for poses.

### 8.5 Wiki Rendering Is Cached

**Decision:** Rendered HTML stored alongside Markdown source. Invalidated on
edit. Scene poses rendered client-side (WASM). Scene archives rendered
server-side (SSR/SEO), cached after first render.

---

## Area 9: Mail & Messaging

### 9.1 Flat List (No Threading)

**Decision:** @mail is a flat list on both web and in-game. No conversation
threading, no grouping. Sorted by date, newest first. Pagination matches
the in-game `@mail` page model.

### 9.2 Same Data, Different View

**Decision:** Web portal reads from the same @mail storage the game uses.
HTTP handler serves mail. No separate mail collection. Portal is a read/write
view into existing game data.

### 9.3 Pages Are Ephemeral

**Decision:** Pages (whispers) appear in the terminal panel as real-time game
output. They are NOT stored, NOT shown in the mail UI, and NOT persisted.
Same semantics as telnet.

### 9.4 Mail Notifications via NATS → SignalR

**Decision:** `portal.mail` NATS event on new mail → SignalR push → badge
count + toast notification. Unread count fetched on page load.

---

## Area 10: Permission & Visibility

### 10.1 Hierarchical Roles

**Decision:** Guest < Player < Royalty < Wizard < God. Higher inherits all
lower permissions. Simple integer comparison. No RBAC framework — just an enum.

### 10.2 Role Derived from Game Flags

**Decision:** Portal role mapped from character flags (WIZARD, ROYALTY, #1).
Set in JWT at login. Changes take effect on token refresh (not instant).
Account-level role = highest among linked characters.

### 10.3 Layout Editing is Wizard+

**Decision:** Players choose color theme only. Layout, nav links, custom CSS
are Wizard+ territory. This keeps the site consistent for all users while
allowing admin customization.

### 10.4 Future Expansion Explicitly Deferred

**Decision:** @powers integration, custom @locks via API, group-based
permissions, per-widget visibility — all deferred. Hierarchy covers current
needs. When needed, they extend (not replace) the hierarchy.

---

## Area 11: URL Strategy

### 11.1 Wiki URLs

**Decision:** `/wiki/Page_Name` — underscores replace spaces, case-insensitive
lookup, display preserves original case. Namespace preserved in URL
(`/wiki/Help:Getting_Started`). Character profiles get alias `/character/Name`.

### 11.2 Scene URLs

**Decision:** `/scenes/42` — numeric ID permalink. Title not in URL (can change).
Active scenes at `/scenes/active`. Live participation at `/scenes/42/live`.

### 11.3 Deep Linking

**Decision:** All pages direct-linkable. No hash routing. Server returns
`index.html` for all non-API routes (standard Blazor WASM hosting fallback).

### 11.4 SEO Pre-rendering

**Decision:** Public pages pre-rendered for bots. Detect user-agent, serve
static HTML with OpenGraph meta tags. Cached 1 hour, invalidated on edit.
Authenticated content returns 403 to bots.

---

## Area 12: Admin Panel

### 12.1 Scope

**Decision:** Blazor panel at `/admin`. Royalty sees players + moderation.
Wizard sees everything. God sees server settings. No duplication of in-game
@-commands — admin panel is for web-native operations only.

### 12.2 Configuration UI

**Decision:** Form-based config editor (not raw text). Sections for general,
limits, feature toggles. Writes to config store, hot-reload where possible.

### 12.3 Audit Trail

**Decision:** Every staff action logged: who, what, target, when. Viewable
at `/admin/moderation/audit`. Searchable, filterable. No silent changes.

### 12.4 Layout Editor Location

**Decision:** Lives at `/admin/layout`. Wizard+ only. Drag-and-drop widget
placement. Preview before publish. Not inline site editing.

---

## Area 13: Widget System

### 13.1 Widget Definition

**Decision:** Widgets are Blazor components implementing `IPortalWidget`.
Each declares: Name, DefaultSize, AllowedZones, optional ConfigSchema.
Registered in DI. Admin places and configures instances.

### 13.2 Zone Model

**Decision:** Five zones: TopBar, LeftSidebar, RightSidebar, MainContent,
Footer. Empty sidebars auto-hide (content goes full-width). Responsive
collapse: sidebars become drawers on tablet, stacked sections on mobile.

### 13.3 Layout Storage

**Decision:** Layout stored as JSON in site config. Per-zone ordered list of
widget instances with per-instance config. One layout for entire site (no
per-page layouts in v1). Cached, invalidated on admin save.

### 13.4 Built-in Widget Set

**Decision:** Ships with: Active Scenes, Recent Wiki Edits, Online Characters,
Quick Links, Welcome Text, Upcoming Events, System Status, Character Switcher.
Custom widgets deferred to Area 18.

---

## Area 14: Search Infrastructure

### 14.1 Search Backend

**Decision:** Graph DB native FTS (ArangoSearch or SurrealDB full-text index).
No external search engine for v1. Sufficient for expected scale (hundreds to
low thousands of documents).

### 14.2 Indexing Pipeline

**Decision:** Index on write. Plain text extracted (Markdig strip or
MString.ToPlainText()) and stored in dedicated field. No background reindex
jobs for normal operation.

### 14.3 Omnisearch UX

**Decision:** Single search bar in TopBar. Debounced 300ms. Instant dropdown
with 2-3 results per type. Enter goes to full results page grouped by type
with snippet context and highlight. Filter by content type available.

### 14.4 Permission Filtering

**Decision:** Filter in query (not post-filter). Users never see content they
can't access. Role and character ID passed as bind parameters. Result counts
and pagination are always accurate.

---

## Area 15: Caching Strategy

### 15.1 Event-Driven Invalidation

**Decision:** NATS events trigger cache invalidation. No TTL on critical
content (wiki, profiles, layout). Edit → event → cache clear → next request
gets fresh. Search results use 60s TTL (approximate is acceptable).

### 15.2 Cache Location

**Decision:** Server-side IDistributedCache (in-memory for single-instance,
Redis for multi-instance). Client-side localStorage for preferences only.
No content cached client-side (SignalR keeps it fresh).

### 15.3 Pre-render Cache

**Decision:** Bot pages rendered lazily on first request, cached 1 hour,
invalidated on content edit. Not pre-generated for all pages.

### 15.4 Cache Warming

**Decision:** On server start: layout config + front page widgets only.
Everything else lazy-populated. No bulk warm-up.

---

## Area 16: Forums / BBS

### 16.1 Softcoded, On-MUSH Storage

**Decision:** BBS is entirely softcoded. Storage lives on-MUSH (game object DB).
Admin customizes commands, display, board structure in-game. Engine doesn't
need to know BBS exists.

### 16.2 HTTP Handler for Read

**Decision:** HTTP handler exposes boards/posts for web display (read-only).
Same pattern as profiles. Respects board read locks per character.

### 16.3 Terminal for Write

**Decision:** No separate web write API. Posting from web goes through the
terminal panel (`+bbs/post` command). Softcode handles all validation and
formatting. Web "New Post" button is a UX shortcut that pre-fills the command.

### 16.4 Post Format

**Decision:** MString (same as mail). Posts can contain ANSI formatting from
in-game. Rendered via `.ToHtml()` on web. Not Markdown.

---

## Area 17: Events & Calendar

### 17.1 Events Are Scheduled Scenes

**Decision:** No separate event system. A scene with `scheduled_start` set is
an event. Same collection, same lifecycle, same commands (with aliases). When
started, becomes a normal active scene.

### 17.2 RSVP

**Decision:** `rsvp_list` field on scene. Statuses: "interested" or "attending".
RSVP via command or HTTP handler (simple toggle, doesn't need terminal routing).
Notifications on scene start to RSVP'd characters.

### 17.3 No Auto-Start

**Decision:** Organizer manually starts the scheduled scene. Scheduled time is
advisory (tells people when to show up). Games are social; strict auto-start
is antisocial.

### 17.4 Calendar Widget

**Decision:** Just a query filter: scenes where `scheduled_start > now()` and
`state = "scheduled"`, ordered by start time. Compact list view. No full
month-grid calendar in v1.

---

## Area 18: Theme Editor

### 18.1 MudBlazor Palette Editor

**Decision:** Admin edits MudTheme palette properties directly (Primary,
Secondary, Surface, Background, Text, etc.) via color pickers with live
preview. Maps 1:1 to MudBlazor's theme system. No custom CSS needed for 90%.

### 18.2 Player Theme Presets

**Decision:** Admin defines named themes (Dark, Light, High Contrast, Custom).
Players pick from the list. Stored in localStorage. Applied before render (no
flash). Colors/typography only — not layout.

### 18.3 CSS Escape Hatch

**Decision:** Wizard+ can add custom CSS block in admin panel. Injected after
MudBlazor theme CSS. Validated (must parse, no script injection). Warning that
it may break on upgrades. Applied site-wide (not per-player).

---

## Area 19: Custom Widgets

### 19.1 Razor Class Library Plugins

**Decision:** Custom widgets ship as .NET RCL DLLs. Same `IPortalWidget`
interface as built-in widgets. Dropped into plugins/ folder, loaded on startup.
Appear in admin widget palette alongside built-ins.

### 19.2 Trusted Code

**Decision:** No sandboxing. Plugin DLLs are trusted (same as NuGet deps).
Only server operators install them. Same trust model as WordPress/Discourse
plugins.

### 19.3 Distribution

**Decision:** No marketplace. Shared via GitHub repos or NuGet packages.
Template repo provided for authoring. Documentation on available services
and the widget interface.

---

## Area 20: Softcode Package Manager

### 20.1 Package Format

**Decision:** Declarative YAML manifest (desired state, not command scripts).
No dbrefs stored — only abstract `~refs` resolved at install time. Convention
prefix advisory, not enforced.

### 20.2 Storage: In-Game Objects

**Decision:** Dedicated PM wizard player owns all package-managed objects.
Objects are normal game objects with no special attrs. PM wizard is the
`@search owner=` mechanism for "list all managed objects."

### 20.3 Storage: System Database

**Decision:** System collections in the game DB (not on disk). Collections:
sys_packages, sys_package_objects, sys_package_depends (edges),
sys_managed_attributes, sys_remotes. Travels with backups. Not visible to
softcode. Tracks per-attribute ownership across packages.

### 20.4 Git Integration

**Decision:** Repos cloned to temp/cache on browse. Per-package commit
tracking (not per-repo). Update detection via `git diff --name-only
<installed_commit>..HEAD -- <path>`. Branch pinning per remote.

### 20.5 Repo Structure

**Decision:** Package = directory. Repo holds one or more packages.
`index.yaml` for fast discovery, fallback to scanning for `package.yaml`.
Monorepos, single-package repos, and curated collections all supported.

### 20.6 Dependencies

**Decision:** Version-constrained dependency declarations. Checked at plan
time. Uninstall blocked if dependents exist. Circular deps are an error.

### 20.7 Three-Way Merge

**Decision:** Base (baseline hash at install) vs. Live (current value) vs.
New (package version). Four outcomes: no-change, auto-upgrade, keep-local,
conflict. Conflicts require admin resolution.

### 20.8 Security Model

**Decision:** Wizard-only. No auto-apply ever. Dangerous pattern scanner
(@force, @toad, etc.) with visual callouts. Trust levels on remotes
(official/community/unknown) with badges.

### 20.9 Admin UX

**Decision:** Web admin panel only, no in-game commands. Consumer flows
(browse, install, upgrade, status, uninstall) and authoring flows (object
picker, dbref resolution, export) all in Blazor.

### 20.10 Default Packages

**Decision:** SharpMUSH official repo ships scene-system, bboard, events,
who-where, finger, http-hooks. These are THE default softcode experience.
The http-hooks package wires up the game→web bridge.

> Decisions 20.11–20.20 were adopted 2026-06-12 from the format review
> (`softcode-package-manager-format-review.md`), which carries the evidence
> and prior-art citations for each.

### 20.11 Ref Syntax (format v2)

**Decision:** Uniform mustache refs, replacing bare sigils: `{{name}}`
(intra-package), `{{$name}}` (well-known), `{{?name}}` (configure),
`{{pkg/ref}}` (cross-package; `pkg` must be a declared dependency).
Case-insensitive end-to-end (defined lowercase, matched any case). Exactly
two braces; `{{{{` escapes a literal `{{`. Every `{{...}}` token must parse
as a valid, resolvable ref — malformed or unresolved tokens are hard errors
(no heuristic warnings). Accepted residual collision: literal MUSHcode
`{{word}}` requires escaping; nested brace groups like `{{a},{b}}` do not
match and are unaffected.

### 20.12 MUSHcode Carrier (format v2)

**Decision:** Block scalars only. Attribute values are written as YAML block
scalars (`|-`); single-quoted strings tolerated for trivial one-liners. The
authoring exporter always emits block scalars. Plain and double-quoted code
is rejected by documentation/lint guidance (double-quote `\` escapes and
plain-scalar `&`/` #`/leading-`@%[{` rules corrupt MUSHcode). No external
payload files — a package manifest is self-contained.

### 20.13 Baselines, Revisions, Rollback

**Decision:** Helm-grade upgrade state. `sys_managed_attributes` stores
`baseline_value` (full text) alongside `baseline_hash` (fast drift index).
New `sys_package_revisions` collection: one record per apply — resolved
manifest snapshot, configure answers, pre-apply values of everything
modified, source commit, version, timestamp, monotonic revision number.
Rollback applies a prior revision's snapshot as a NEW revision. Configure
answers persist across upgrades (no re-prompting). Retention configurable
(default 10).

### 20.14 Releases via Git Tags

**Decision:** Go-modules monorepo convention: releases are tags named
`<package-dir>/v<semver>` (e.g. `who-where/v1.2.0`). Version list = tag
list; installs pin tags; HEAD is the dev channel for untagged repos.
Release tags are immutable (CI-enforced in the official repo). Consumer-side
moved-tag detection: if the tag for the installed version no longer points
at `installed_commit`, show a loud trust warning before any changeset.

### 20.15 Changeset Classification (extended)

**Decision:** Plan engine classifies beyond value modification: deletes
(attr/object removed in new version), modify/delete conflicts, add/add
conflicts (package adds an attr that already exists locally with different
content). Renames never become destroy+create: per-object
`previous_refs: [old_name]` and package-level `replaces: old-package-id`
hints preserve dbrefs and registry continuity.

### 20.16 Version & Constraint Semantics

**Decision:** SemVer 2.0.0 item-11 prerelease ordering (dot-split
identifiers; numeric compared numerically and always lower than
alphanumeric; fewer fields lower on equal prefix). node-semver prerelease
range rule: a prerelease version satisfies a constraint only if some clause
carries a prerelease on the same [major,minor,patch] tuple — prereleases are
never selected implicitly. No `^`/`~` operators (targeted error messages
suggest `>= <` form). Build metadata (`+`) unsupported with a clear error.

### 20.17 Manifest Metadata (format v2)

**Decision:** `format:` field (missing = 1; unknown minor → warn, unknown
major → reject — the Python Metadata-Version rule). New fields: `license`,
`homepage`, `keywords` (≤5 advisory), `requires_server` (engine version
constraint, checked at plan time). Exits become expressible: `location:`
(required for exits, optional for things/players, forbidden for rooms) and
`destination:` (exits only). `index.yaml` entries may carry optional
`package`/`version`/`description` for cheap browsing and duplicate-id CI.

### 20.18 Package Identity

**Decision:** Flat ids (≤64 chars, `[a-z][a-z0-9-]*`) bound to their source
repo at install. Installing an id from a different repo than recorded is a
hard error with guidance. Provenance always shown in UI. Official-repo CI
enforces npm-style moniker rule (punctuation-collapse collision check).
`package` is reserved as a dependency id.

### 20.19 Configure Parameters (typed)

**Decision:** Typed configure parameters in v1: `type: dbref | string |
number | boolean` (default `dbref`) with optional `default` (forbidden for
dbref type — portability is the point of configure). Only dbref-typed refs
may appear in `parent:`/`location:`/`destination:`. Undeclared `{{?name}}`
usage is a hard error (explicit syntax removes the old wildcard ambiguity).
Answers persisted in revisions (20.13). `pattern` reserved for future
validation.

### 20.20 Conflicts & Reserved Relationships

**Decision:** `conflicts:` honored at plan time (same forms as `depends`
minus source hints). The plan engine additionally auto-detects `$command`
pattern collisions across installed packages (the MUSH analog of file
conflicts) and flags them in review. `provides:`, `recommends:`, `suggests:`
are reserved keys — parsed and ignored with a warning until specified.

---

## Design Documents Index

| Document                          | Status    | Covers                              |
|-----------------------------------|-----------|-------------------------------------|
| web-portal-vision.md              | Complete  | Architecture, layout, widgets, phases|
| wiki-shared-content.md            | Complete  | Wiki schema, dual-render, @wiki     |
| front-page-and-navigation.md      | Complete  | Front page, omnisearch, help, onboard|
| ui-patterns.md                    | Complete  | 17 UX patterns + anti-patterns      |
| architectural-decisions.md        | Complete  | This document                       |
| scene-system.md                   | Complete  | Scene lifecycle, UI, real-time       |
| character-profiles.md             | Complete  | Profile pages, sheets, gallery       |
| content-rendering-pipeline.md     | Complete  | MString shared lib, Markdig, sanitization|
| mail-messaging.md                 | Complete  | Flat @mail on web, notifications     |
| permission-visibility.md          | Complete  | Hierarchical roles, per-system tables|
| url-strategy.md                   | Complete  | Routes, deep links, SEO              |
| admin-panel.md                    | Complete  | Admin sections, config UI, audit     |
| widget-system.md                  | Complete  | Zones, IPortalWidget, layout JSON    |
| search-infrastructure.md          | Complete  | FTS, omnisearch, permission filtering|
| caching-strategy.md               | Complete  | Event invalidation, cache layers     |
| forums-bbs.md                     | Complete  | Softcoded BBS, HTTP read, terminal write|
| events-calendar.md                | Complete  | Events = scheduled scenes, RSVP      |
| theme-editor.md                   | Complete  | MudTheme palette, player presets, CSS|
| custom-widgets.md                 | Complete  | RCL plugins, IPortalWidget, loading  |
| softcode-package-manager.md       | Complete  | Package format, git, storage, merge  |
| softcode-package-manager-ux.md    | Complete  | Admin panel UX, authoring, review    |
