# Softcode Package Manager

## Overview

A git-backed package management system for MUSH softcode. Admins install,
upgrade, and author softcode packages through the Blazor admin panel. Packages
are directories in Git repos — single-package repos, monorepos (Volund's suite),
or curated community collections all work.

The MUSH is a live production system with no deploy boundary. This system adds:
- Preview before apply (plan/apply model)
- Three-way merge on upgrade (respects local customizations)
- Dependency chains between packages
- Dangerous pattern scanning for security review
- MUSHcode syntax highlighting in all review UIs

## Problem Statement

Softcode distribution has been "paste a .mush file" for 25+ years:
- No versioning, no upgrade path
- Installing means pasting hundreds of lines hoping dbrefs align
- Customizations overwritten on upgrade (really: reinstall from scratch)
- No dependency tracking between systems
- No way to preview what an install/upgrade will do to a live MUSH

## Architecture: Plan/Apply Model

NOT a staging database approach. The full-universe staging DB is for
backup/restore. This uses a lightweight plan/apply model:

```
┌──────────┐     ┌──────────────┐     ┌──────────┐
│  Package │     │   Changeset  │     │  Live DB │
│ Manifest │────▶│  (pure data) │────▶│  (apply) │
└──────────┘     └──────┬───────┘     └──────────┘
                        │
                  ┌─────▼─────┐
                  │  Admin UI │
                  │  (review) │
                  └───────────┘
```

### Phase 1: Plan (read-only)

- Read live DB state for objects/attrs relevant to the package
- Compare to desired state in package manifest
- Produce a changeset (pure data, never materializes in DB):
  - Objects to create (abstract names, resolved to dbrefs at apply time)
  - Attributes to set (new)
  - Attributes to modify (value differs from installed baseline)
  - Conflicts (attr modified locally AND changed in new version)

### Phase 2: Review

- Admin sees changeset with conflict annotations in Blazor admin panel
- For each conflict: keep mine / take theirs / edit merged value
- Can reject individual non-conflicting changes
- Dangerous patterns flagged visually
- Wizard-flagged objects prominently warned

### Phase 3: Apply

- Execute resolved changeset as individual DB operations against live
- Update installation record with new baselines
- Record the git commit applied from

## Package Format

Declarative manifest of desired state (not a command script):

```yaml
package: myrddins-bbs
version: 2.4.1
authors: [Myrddin]
description: "Bulletin Board System"
convention_prefix: BBS_       # advisory namespace, not enforced

depends:
  - volund-core: ">=1.0"      # dependency with version constraint

objects:
  - ref: bbs_global           # abstract name — NOT a dbref
    type: thing
    name: BBS Global Object
    parent: ~bbs_parent       # ~ prefix = intra-package reference
    flags: [no_command]
    attributes:
      CMD_+BBREAD:
        value: "$+bbread *:@pemit %#=[u(~bbs_parent/FN_READ,%0)]"
        flags: []
      FN_READ:
        value: "..."

  - ref: bbs_parent
    type: thing
    name: BBS Parent Object
    attributes:
      FN_FORMAT:
        value: "..."
```

**Key rules:**
- No dbrefs stored — only abstract `~refs` resolved at install time
- Intra-package references use `~ref_name` prefix
- External references: `$well-known` (master room) or `?configure` (installer provides)
- Convention prefix is advisory (helps avoid collisions, not enforced)

## Repo Structure

A package is a directory. A repo contains one or more packages.

```
# Single-package repo
myrddins-bbs/
├── package.yaml
└── objects/...

# Multi-package repo (Volund's suite)
volund-mush-suite/
├── index.yaml            ← lists packages, repo metadata
├── core/
│   ├── package.yaml
│   └── objects/...
├── bbs/
│   ├── package.yaml
│   └── objects/...
├── jobs/
│   └── package.yaml
└── mail/
    └── package.yaml

# Community collection
sharpmush-packages/
├── index.yaml
├── bbs-myrddin/
│   └── package.yaml
├── jobs-anomaly/
│   └── package.yaml
└── chargen-faraday/
    └── package.yaml
```

Discovery: read `index.yaml` if present, else scan for `package.yaml` files.

## Git Integration

### Commit Tracking (Per-Package)

Each installed package tracks its own commit independently:

```
sys_packages:
  { id: "volund-bbs",
    source_repo: "https://github.com/volund/mush-suite",
    source_path: "bbs/",
    installed_commit: "a3f8c1d",
    installed_version: "1.0.0",
    pinned_branch: "stable" }
```

### Update Detection

```bash
# Cheap check: did anything in this package's path change?
git diff --name-only a3f8c1d..HEAD -- bbs/
```

If files changed in the package's path since installed commit → "update
available." Changes in sibling packages (e.g., jobs/) don't trigger.

### Repo Cache

Repos cloned to a temp/cache directory. Pulled on browse/check-for-updates.
These repos are tiny (YAML + text) — full git history enables cheap diffing.

**Cache location:** System temp directory (configurable, default: `/tmp/sharpmush-packages/`)

Repos are re-pulled when the admin opens the package browser. Not background-polled.

## Storage Model

### In the Game World

- A dedicated **Package Manager wizard** (player object) owns all package-managed objects
- Objects are normal game objects — no special attrs on them
- Attributes owned by the PM wizard
- `@search owner=<PM_WIZARD>` instantly lists all managed objects

### In System Database (same DB, separate collections)

Package metadata lives in system collections — not visible to softcode,
travels with backups, queryable:

```
Collection: sys_packages
  { id: "volund-bbs", version: "1.0.0", source_repo: "...",
    source_path: "bbs/", installed_commit: "a3f8c1d",
    installed_at: "2025-06-05T01:00:00Z", pinned_branch: "stable" }

Collection: sys_package_objects
  { package: "volund-bbs", ref: "bbs_global",
    objid: "#907:1778518155494", type: "thing" }

Edge collection: sys_package_depends
  { _from: "volund-bbs", _to: "volund-core", constraint: ">=1.0" }

Collection: sys_managed_attributes
  { package: "volund-bbs", objid: "#907:1778518155494",
    attr: "CMD_+BBREAD", baseline_hash: "a3f8c1...",
    baseline_version: "1.0.0" }
```

**Why DB, not disk:**
- Travels with backups (restore = package registry matches game state)
- Queryable (dependency resolution, drift detection)
- Not visible to softcode (system collections, not game objects)
- Consistent across all three backends

## Three-Way Merge (Upgrade)

| Base == Live | Base == New | Action                        |
|:---:|:---:|---|
| ✓ | ✓ | No change needed                       |
| ✓ | ✗ | Auto-upgrade (user didn't touch it)    |
| ✗ | ✓ | Keep local (package didn't change it)  |
| ✗ | ✗ | **CONFLICT** — needs admin decision    |

Base = value at last install/upgrade (baseline hash in sys_managed_attributes).
Live = current value in game DB. New = value in new package version.

## Cross-Package Attribute Ownership

A package can manage attributes on objects it doesn't own:

```
# volund-bbs manages an attr on volund-core's object
sys_managed_attributes:
  { package: "volund-bbs",
    objid: "#905:...",          ← volund-core's object
    attr: "CMD_+BBADMIN",
    baseline_hash: "d4e5f6..." }
```

The PM wizard owns the objects (in-game ownership). The system DB tracks which
package put which attribute where (finer-grained ownership).

## Dependency Resolution

Before installing a package:
1. Read its `depends:` list
2. Check sys_packages for each dependency
3. Verify version constraints are met
4. If unmet: show "requires volund-core >=1.0 — install it first?" with link

Circular dependencies are an error. Uninstalling a package checks for
dependents ("volund-bbs depends on this — uninstall it first or force-remove").

## Remotes Configuration

```
sys_remotes:
  - name: "SharpMUSH Official"
    url: "https://github.com/sharpmush/packages"
    trust: official
    branch: main
  - name: "Volund's Suite"
    url: "https://github.com/volund/mush-suite"
    trust: community
    branch: stable
  - name: "Myrddin's BBS"
    url: "https://github.com/myrddin/pennmush-bbs"
    trust: community
    branch: main
```

Trust levels: `official` (SharpMUSH-org maintained, green badge),
`community` (known, yellow), `unknown` (red warning).

## Security

### Access Control

- **Wizard-only.** Packages can contain wizard-flagged objects with root access.
- **No auto-apply, ever.** Every install/upgrade goes through review screen.
- **No in-game commands.** All operations are web admin panel only.

### Dangerous Pattern Scanner

Attributes containing these patterns are visually flagged in the review UI:

```
@force    @toad    @newpassword   @nuke    @boot
@pcreate  @halt    pemit(*,       @wall    @shutdown
```

Flagged attrs get a warning callout: "⚠️ Contains: @force — review carefully"

Not a blocker — just a visual alert. Wizards know what they're looking at,
but the callout ensures nothing slips past during bulk review.

### Trust Indicators

- Official repos: green badge, no warning
- Community repos: neutral, source URL shown
- Unknown/first-time repos: yellow warning banner: "This source has not been
  reviewed. Inspect all code before applying."

## Default Packages (SharpMUSH Official)

The SharpMUSH official repo ships curated default softcode:

```
sharpmush-packages/
├── index.yaml
├── scene-system/          ← +scene commands, HTTP handler hooks
├── bboard/                ← +bbs commands, HTTP handler hooks
├── chargen/               ← +sheet, profile system
├── events/                ← +events (scheduled scene wrapper)
├── who-where/             ← +who, +where
├── finger/                ← +finger (profile summary)
└── http-hooks/            ← Base HTTP handler event objects
```

These packages ARE the "default softcode experience." New installs can
`+package/install-official` (or equivalent web action) to bootstrap a
full-featured game. They also serve as reference implementations for
package authors.

The `http-hooks` package is special — it provides the event objects that
the web portal's HTTP handlers expect (profiles, scenes, BBS). Installing
it wires up the game → web bridge.
