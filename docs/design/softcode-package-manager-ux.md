# Softcode Package Manager — Admin UX

## Interface Location

All package management lives in the Blazor admin panel at `/admin/packages`.
Wizard-only access. No in-game commands for package operations.

## Why Web-Only

- Diffs are visual — syntax highlighting, side-by-side comparison
- Conflict resolution is interactive — radio buttons, merge editor
- Package browsing needs search, filtering, version lists
- Authoring requires multi-select, relationship graphs, batch editing
- Security review needs prominent visual callouts for dangerous patterns
- MUSHcode syntax highlighting throughout

## Consumer Flows

### 1. Browse/Search (`/admin/packages/browse`)

- Lists configured remotes with trust badges
- Search by name, author, description across all remotes
- Each package shows: name, version, description, trust badge, dependency list
- Click → detail page with README, version history, object list preview

### 2. Install (`/admin/packages/browse/{package}` → review)

- Select package + version (defaults to latest)
- System checks dependencies ("requires volund-core >=1.0")
- Computes changeset (plan phase — read-only against live DB)
- Navigates to review screen

### 3. Review Screen (shared by install and upgrade)

```
┌───────────────────────────────────────────────────────────────────┐
│ Install: myrddins-bbs v2.4.1                    [Apply] [Cancel] │
│ From: SharpMUSH Official ✓                                       │
├────────────────────────┬──────────────────────────────────────────┤
│ Changes (14)           │ BBS Global / CMD_+BBREAD                 │
│                        │                                          │
│ ● BBS Global     [new] │ Status: ★ NEW                           │
│   ├ CMD_+BBREAD    ★   │                                         │
│   ├ FN_READ        ★   │ ┌─ Value ────────────────────────────┐  │
│   ├ FN_POST        ★   │ │ $+bbread *:@pemit %#=              │  │
│   └ FN_DELETE      ★   │ │   [u(~bbs_parent/FN_READ,%0)]     │  │
│                        │ └────────────────────────────────────┘  │
│ ● BBS Parent     [new] │                                         │
│   └ FN_FORMAT      ★   │ Flags: (none)                          │
│                        │                                          │
│ Legend:                 │ [✓ Accept] [✗ Reject]                   │
│ ★ New                  │                                          │
│ ✓ Auto-safe            │                                          │
│ ⚠ Conflict             │                                          │
│ 🔴 Danger              │                                          │
└────────────────────────┴──────────────────────────────────────────┘
```

For upgrades, conflicts show three-pane view:

```
┌─ Base (v2.4.1) ─────────────────┐
│ $+bbread *:@pemit %#=           │
│   [u(FN_READ,%0)]              │
├─ Live (your version) ───────────┤
│ $+bbread *:@pemit %#=           │
│   [ansi(hw,=== %0 ===)]        │  ← you added this
├─ New (v2.5.0) ──────────────────┤
│ $+bbread *:@pemit %#=           │
│   [u(FN_HEADER,%0)]            │  ← package changed this
└─────────────────────────────────┘

(○) Keep mine  (●) Take theirs  (○) Edit manually
```

### 4. Status Dashboard (`/admin/packages`)

- Table: installed packages, version, install date, source, trust badge
- Per-row indicators: "N locally modified attrs", "update available" badge
- Expand row → list of modified attrs with quick diff
- Actions per package: upgrade, uninstall, view in repo

### 5. Upgrade Flow

- "Update available" badge on status dashboard
- Click → shows what changed in the package since installed commit
- Computes three-way changeset → navigates to review screen
- Review resolves conflicts → apply → baselines updated

### 6. Uninstall (`/admin/packages/{id}/uninstall`)

- Preview: "These objects would be @destroyed, these attrs removed from shared objects"
- Lists dependents if any: "volund-bbs depends on this — cannot uninstall"
- Confirm → apply → objects destroyed, sys_ records removed

## Authoring Flows

### Create Package from Live Objects (`/admin/packages/author`)

#### Step 1: Object Picker

- Multi-select objects by: dbref input, name search, zone filter, owner filter
- As objects are selected, system shows relationships:
  "Selected objects reference each other: #847 → #848 (via u(#848/FN))"
- Suggestion: "Object #849 is referenced by attrs in your selection but not
  included. Add it?"
- Exclude system objects (PM wizard, master room, etc.) from selection

#### Step 2: Dbref Resolution

The core authoring challenge. Scans all attr values for `#\d+` patterns.

Auto-classified:
- `~internal` — the dbref is another object in the selection (auto-resolved)

Admin must classify:
- `$well-known` — standard object (master room #0, etc.)
- `?configure` — game-specific; installer will be prompted to provide

**Batch resolution UI:**
```
┌──────────────────────────────────────────────────────────┐
│ Unresolved References (3 unique dbrefs, 17 occurrences)  │
├──────────────────────────────────────────────────────────┤
│ #848 (BBS Parent) — 12 occurrences                       │
│   [Auto: ~bbs_parent — object is in your selection]      │
│                                                          │
│ #0 (Room Zero) — 3 occurrences                           │
│   (●) Well-known: $room_zero                             │
│   (○) Configure: installer provides                      │
│                                                          │
│ #123 (Game Config Object) — 2 occurrences                │
│   (○) Well-known: ________                               │
│   (●) Configure: installer provides                      │
│         Label: "Game config object"                      │
└──────────────────────────────────────────────────────────┘
```

#### Step 3: Attribute Selection

- Per-object: expandable attr list with checkboxes
- Default: all included
- Heuristic highlights for "probably local" attrs (DESCRIBE, DOING, LAST_*)
- Admin unchecks attrs that aren't part of the package

#### Step 4: Metadata

- Package name (slug: lowercase, hyphens)
- Version (semver)
- Description
- Authors
- Convention prefix (advisory)
- Dependencies (select from known packages)
- README (markdown editor)

#### Step 5: Export

- Generates package.yaml manifest
- Validates: all refs resolve, no unclassified dbrefs, deps exist
- Export to: push to Git remote OR download as zip
- Creates git commit with appropriate message

### Update Existing Package (`/admin/packages/author/{id}`)

For package maintainers iterating:
- Load existing manifest from installed package record
- Diff live state vs. manifest ("these attrs changed since last export")
- Toggle which changes to include in new version
- Bump version → re-export → push to remote

## MUSHcode Syntax Highlighting

The review UI highlights MUSHcode in attribute values:
- Functions: `u()`, `setq()`, `get()`, etc. — colored
- Substitutions: `%#`, `%0`-`%9`, `%q<N>` — colored differently
- Commands: `@pemit`, `@force`, `think` — keyword colored
- Strings: everything else — default text color
- Dbrefs: `#\d+` — link-colored, clickable (shows what it resolves to)

Dangerous patterns additionally get a background highlight (red/orange tint)
with an icon in the gutter.

## Notifications

- **No in-game notifications.** Admin checks the web panel.
- **On visit:** status dashboard shows "N updates available" count
- **Optional:** system could check remotes on a schedule and show a count
  next to "Packages" in the admin nav — deferred to v2.
