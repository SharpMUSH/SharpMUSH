# Implementation Dependency Graph & Parallel Tracks

## Dependency Analysis

### Notation
- `A → B` means "B depends on A" (must finish A before starting B)
- Items at the same layer can run in parallel

## Layer 0: Project Scaffolding (no deps)

These are pure setup — can all happen simultaneously:

| Track | Area | Work |
|-------|------|------|
| A | 11 (partial) | Blazor WASM project, MudBlazor, basic route structure |
| B | 8 | Markdig pipeline setup (MString.ToHtml() already exists) |
| C | 18 (partial) | MudTheme wiring, ThemeProvider, base dark theme |

**Why parallel:** No runtime dependencies between them. Project structure,
rendering lib, and theming are all independent scaffolding.

## Layer 1: Foundation (depends on Layer 0)

| Track | Area | Work | Depends On |
|-------|------|------|------------|
| A | 1 | ASP.NET Identity, JWT, account/character model | Project exists |
| B | 2 | SignalR hub, NATS bridge service | Project exists |
| C | 10 | Permission hierarchy enum, role middleware | Project exists |

**Why parallel:** Auth, Transport, and Permissions are orthogonal systems.
Auth doesn't need SignalR. Transport doesn't need auth (yet). Permissions
is just an enum + attribute/middleware — no runtime deps on the other two.

**Note:** Area 10 is tiny — it's an enum + a `[RequireRole(Wizard)]` attribute.
Could be done inside Area 1 as a subtask.

## Layer 2: Core Infrastructure (depends on Layer 1)

| Track | Area | Work | Depends On |
|-------|------|------|------------|
| A | 3 + 4 | Client state services, REST controllers, SignalR contract | Auth (1), Transport (2) |
| B | 15 | IDistributedCache, NATS subscription for invalidation | Transport (2) |
| C | 12 (shell) | Admin panel layout, route guard, basic nav | Auth (1), Perms (10) |
| D | 20 (backend) | sys_ collections, package manifest parser, plan engine | DB layer (exists) |

**Why parallel:**
- A is the "how clients talk to the server" plumbing
- B is cache infra (subscribes to NATS, no client interaction yet)
- C is just the admin shell (authenticated frame, no content yet)
- D is entirely backend — DB schema + parsing logic, no UI needed yet

**Key insight:** Area 20 backend (collections, parser, plan engine) has NO
dependency on Auth or Transport. It's pure server-side DB + git operations.
Can start as soon as the DB layer is accessible (which it already is).

## Layer 3: Features (depends on Layer 2)

| Track | Area | Work | Depends On |
|-------|------|------|------------|
| A | 5 + 6 | Wiki pages, character profiles (profiles ARE wiki pages) | API (4), Rendering (8), Perms (10) |
| B | 7 | Scene system (lifecycle, poses, SignalR events) | API (4), Transport (2), Perms (10) |
| C | 9 | @mail web view, notifications | API (4), Transport (2) |
| D | 13 | Widget system (IPortalWidget, zones, layout JSON) | Admin shell (12) |
| E | 20 (UI) | Admin package browser, review screen, authoring | Admin shell (12), pkg backend (20-backend) |

**Why parallel:**
- Wiki/Profiles (A) and Scenes (B) don't interact at all at this layer
- Mail (C) is completely independent of wiki and scenes
- Widget system (D) only needs the admin frame to live in
- Package manager UI (E) only needs admin frame + its own backend

**Note:** A is really 5 then 6 in sequence (profiles depend on wiki existing).
B and C are fully parallel with A.

## Layer 4: Polish & Advanced (depends on Layer 3)

| Track | Area | Work | Depends On |
|-------|------|------|------------|
| A | 14 | Search (index wiki, profiles, scenes) | Wiki (5), Profiles (6), Scenes (7) |
| B | 16 | BBS web view (HTTP handler read) | Pkg manager (20) — BBS is a default package |
| C | 17 | Events = scheduled scenes | Scenes (7) |
| D | 19 | Custom widget plugin loading | Widget system (13) |
| E | 20 (official pkgs) | Author default packages (scenes, bbs, etc.) | Pkg manager complete, features exist |
| F | 21 | Dynamic Applications (schema-driven forms/views + registry) | Widget system (13), HTTP handler/Profiles (6), Pkg manager (20) |

**Why parallel:**
- Search needs content to exist but doesn't care about BBS/events
- BBS web view depends on the softcode being installable (pkg manager)
- Events are just a scene extension
- Custom widgets are just a plugin loader on top of the widget system
- Dynamic Applications reuse the profile schema/HTTP-handler pattern (6), plug into the
  widget system (13), and ship schemas as packages (20) — no new server transport

## Critical Path (longest chain)

```
Scaffolding → Auth (1) → API (3+4) → Wiki (5) → Profiles (6) → Search (14)
                                    → Scenes (7) → Events (17)
```

This is the spine. Everything else branches off it.

## Recommended Parallel Workstreams

For a single developer with multiple worktrees/branches:

### Stream 1: "Portal Core" (the critical path)
Layer 0 → 1(Auth) → 2(API+State) → 3(Wiki→Profiles, Scenes, Mail)

### Stream 2: "Infrastructure" (independent backend)
Layer 0 → Transport(2) → Caching(15)
                        → Widget system(13)
                        → Theme finalization(18)

### Stream 3: "Package Manager" (mostly independent)
Layer 0 → DB collections → Manifest parser → Plan engine → Apply engine
        → Git integration → Admin UI (needs admin shell from Stream 1)

### When streams converge:
- Stream 2 merges into Stream 1 once admin shell exists (Layer 2)
- Stream 3's UI merges into Stream 1 once admin shell exists
- Layer 4 (search, events, BBS) only starts when Stream 1 has features
- Default packages (Stream 3 output) need Stream 1 features to exist first

## Practical Suggestion

Start THREE branches simultaneously:

1. `feature/portal-auth-api` — Areas 1, 3, 4, 10 (the plumbing)
2. `feature/portal-infra` — Areas 2, 8, 11, 15, 18 (transport, rendering, caching, theme)
3. `feature/package-manager` — Area 20 backend (collections, parser, plan/apply engine)

These three can be developed fully independently until Layer 3, where they
merge and features start getting built on top of all three together.
