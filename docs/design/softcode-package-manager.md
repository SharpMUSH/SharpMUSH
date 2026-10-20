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

### Roles and permissions (format 1.2)

A manifest may also declare `categories:` (role and permission), `permissions:` (custom
permissions) and `roles:`. The reference is
`examples/packages/README.md`; the implementation is `IPackageDeclarationService` with the pure
`PackageDeclarationPlanner`. The decisions:

- **Ownership.** An item the package creates is recorded in `InstalledPackageRecord.Owned`, with
  the values that version declared. An item the game already has is the game's: the package uses
  it and never changes or removes it. One owned by another installed package blocks the plan.
- **No built-in grants.** A package role sets only custom permissions and has a priority below the
  wizard role. Installing is a `packages.admin` act, and a role assigned later by a `roles.admin`
  holder must not carry powers that holder could not grant.
- **Upgrade** is a field-wise three-way merge against `Owned`: the new value where the version
  changed a field, the live value otherwise. A role's permissions merge per scope.
- **Removal** (a dropped item, or uninstall) deletes a role, permission or
  category unless something remaining relies on it (a holder, a role setting it, a member), in
  which case it is kept and released. Permission removal clears its overrides, so removals run last.
- **Rollback** restores objects and attributes only.

### Configuration options (format 1.3)

`settings:` maps an option name, as `@config` lists it, to a value or, for a dbref option, one
object ref. The reference is `examples/packages/README.md`; the implementation is
`IPackageSettingService`, which writes through `IConfigOptionWriter` (the same parse, range check,
validators and reload as `@config/set`). The decisions:

- **Validity.** The manifest reader checks the option exists, is settable, and that a literal parses
  for its type. The plan checks the value against the live configuration (`PreviewAsync`): a value
  that would be clamped or that a validator refuses blocks it.
- **Conflicts.** One option has one package setting it. An option another installed package
  records in `InstalledPackageRecord.Settings` blocks the plan.
- **Ownership.** The record keeps the value set and the value it replaced. An upgrade writes only a
  value the version changed. A dropped option, and every option on uninstall, is put back only while
  it still holds the package's value; an administrator's change is left alone.
- **Confirmation.** The review screen shows each option's value now and after, and apply waits for
  the installer to confirm when an option would change.
- **Rollback** leaves options as they are.

### Wiki pages (format 1.4)

`wiki:` lists pages a package ships: each `page: namespace:slug` (one key; the namespace, required and
lower case, is `main`, `help` or `category`; the slug already in slug form), a title, categories, the per-page `requires:` (view, edit, delete) and the pin of a category page,
with its body in a package file or inline. A package with `wiki:` may leave out `objects:`: a
content pack. The reference is `examples/packages/README.md`. The decisions:

- **Bodies are read once.** `file:` bodies come through `IPackageFileSource` (a git commit, the
  upload folder, a directory for the validators) when the manifest is fetched, through
  `PackageWikiBodies`, strict UTF-8, at most 1 MB each. The plan carries the text, so review and
  apply see the same pages. The bundled catalogue embeds manifests only and has no file source:
  no bundled package ships wiki pages.
- **Links are full.** `PackageWikiLinks` walks each body with the wiki's own Markdig pipeline
  (`WikiLinkInline.Target`) and requires every `[[Target]]` / `[[Display|Target]]` to be
  `Namespace:slug`: a bare or title-cased target is an error naming the file and the link. A
  full link to an unshipped page that looks like a typo for a shipped one is a warning; links
  outside the package are allowed. Inline bodies are checked when the manifest is read, file
  bodies when `PackageWikiBodies` reads them, so the server and both validators agree.
- **Softcode never reads wiki data.** Page text is literal Markdown with no `{{ref}}`
  substitution, and there is no function that reads it. A table softcode rolls on is attributes in
  a softcode package.
- **Ownership.** `IPackageWikiService` (pass 4d, after settings, so a page may require a
  permission the package's own `permissions:` creates in pass 4b) records each owned page in the
  Lightning table `pkg.wiki` as a `PackageWikiPageRecord`: every field as the package last wrote
  it, which is the baseline. One page has one package: a page another package owns blocks the plan.
- **Plan.** `PackageWikiPlanner` is pure, one row per page. No baseline: create when absent, adopt
  when the game has it exactly so, otherwise an add-add conflict (keep mine leaves the page
  unowned). With a baseline: a field-wise three-way merge — the body by line diff3 (`LineMerge`,
  LCS with the shared ends trimmed), title and published as values, categories, requirements and
  the pin as sets. A clash in the body, title or published flag is a modify-modify conflict; the
  review screen shows base, live, new and the merge with each clash marked, and keep mine takes
  every non-clashing change of the version while keeping the game's side of each clash. A page
  staff deleted stays deleted when the version did not change it, and is a delete-modify conflict
  when it did. The baseline always advances to the new version.
- **Conflicts** travel as `PackageConflictDecision` with `TargetRef = "wiki:<namespace>"` and the
  page as `Attribute`, so the apply endpoint takes them like attribute conflicts.
- **Writes.** Pages are written through `IWikiService` as the Package Manager wizard, the revision
  summary naming the package and version; a title change uses `SetTitleAsync`, which writes no
  revision and never moves the slug. Each write is tracked by `PackageWriteTransaction` with an
  inverse that applies only while the field still holds what the apply wrote.
- **Release.** A page the version drops, and every page on uninstall, is deleted when untouched
  (every field equals the baseline and no translation exists), and its category unpinned if the
  package pinned it and it is still pinned. A changed page stays as the game's and its row goes.
  Deletes run after the operation commits, since a deleted page keeps neither id nor history; the
  pre-package backup covers them.
- **Rollback** leaves wiki pages as they are; a page's history is the way back for its text.

### Wiki collection display name (format 1.13)

An optional top-level `wiki_collection_name` supplies the public label for the package's wiki collection. It is a scalar string trimmed to 1–100 characters, with control characters and line/paragraph breaks refused. An explicit empty or null value is invalid; omit the key to use the existing package-ID label with hyphens replaced by spaces. Formats below 1.13 warn and ignore this field before validating its value.

The label is metadata, separate from the package ID, collection URL and wiki ownership. It is persisted on the installed package and in each revision snapshot. An apply or upgrade replaces it, including clearing it when absent; rollback restores the selected snapshot's value, with null as the default for legacy snapshots. The Lightning registry's nullable field reads older records without a migration. Collection navigation and result-row context use the same label lookup; authorization still decides which collections/pages are visible.

### Wiki collection entrance (format 1.14)

An optional top-level `wiki_landing_page` selects one page declared by the same manifest's `wiki:` block:

```yaml
format: 1.14
package: independent-rules
version: 1.0.0
wiki_landing_page: 'main:guide'
wiki:
  - page: main:guide
    title: Rules guide
    markdown: Start here.
```

The value is one nonempty, single-line scalar, with surrounding whitespace trimmed and nothing else normalized. It uses the same canonical authored `namespace:slug` parser as `wiki[].page`: lowercase `main`, `help` or `category`, and an unchanged slug of at most 200 UTF-8 bytes, with no whitespace, controls, slash, backslash, colon, URL query or fragment. A null, list, mapping, undeclared key or duplicate/ambiguous target is invalid. Softcode and application packages may declare it; plugin and UI packages may not. Formats at or below 1.13 warn and ignore the field before inspecting its value, including malformed nodes. A newer minor format still produces its independent format warning.

Omitting the key means no authored entrance. Removing it on an upgrade clears the entrance; changing it explicitly requests a new binding to the newly named declared page. An installed entrance must bind the actual owned page identity rather than following a later page recreated at the same slug. Installed binding, rollback and read authorization belong to the installer and collection reader; the manifest stores only the authored target. Neither a title, category pin nor `wiki_collection_name` selects an entrance. `homepage` remains the external project/documentation URL and is never used as this target.

`PackageManifestService.ValidateWikiLandingPage(PackageManifest)` checks programmatically supplied landing metadata with the same validation core as parsing. The installer checks its errors before any writes or managed plugin/UI deployment. Its read-only preview reports invalid metadata as blocking issues and describes additions, changes and removals using the canonical target keys. The writer checks it before producing YAML, rejects inconsistent metadata with `ArgumentException`, quotes the canonical scalar and preserves the selected format. Existing manifests without this field retain their format and output.

After wiki apply, the installer corroborates the declared target against this package's ownership row and the live page's exact ID, namespace and slug. It records `WikiLandingPage` as `{ Namespace, Slug, PageId }` in the existing tracked installed record and revision snapshot. A failed apply or cancellation before revision commit uses the existing compensation log to restore the old metadata and ownership. Package operations remain compensating operations, with their existing intermediate visibility and crash-recovery limitations.

An update retaining the same authored target cannot bind a different PageId, even when a replacement's content is identical. To deliberately bind a replacement, successfully clear the field first and explicitly re-add it in another apply, or select another declared target. Missing ownership may be repaired by the ordinary planner's explicit adoption of the original still-live ID. Omitting the field clears metadata without independently deleting its page. Uninstall removes the installed binding; existing page-release rules decide whether a page is deleted or retained as game content.

Rollback restores the selected snapshot's captured exact binding only while its ownership and live identity still match. Otherwise it clears the installed binding, reports why to the administrator and records that absence in the new rollback revision. Rollback does not recreate, re-adopt or rewrite wiki pages. Legacy installed records and snapshots missing the optional field read as null; this additive JSON change needs no table, backfill or migration. Navigation supplies a compact authorized live localized descriptor; the portal renders it as Start here before reference browsing. The canonical article read can enforce its exact `expectedPageId`. Optional existing OAuth authentication on these reads and uncached delegated/current account authority checks preserve token-scope limits. Preparation and final finite page/ownership/translation/policy rechecks withhold changed selections; these ordinary service reads do not establish a coherent snapshot or a storage-work bound.

Format change note: 1.14 adds `wiki_landing_page` and the shared authoring validation seam. Format 1.13 collection-name behavior is unchanged. This format number does not assert a released server version or add an inferred `requires_server` requirement.

### Attribute entries (format 1.5)

`attributes:` maps an attribute name to the standard attribute entry `@attribute` would make:
default `flags`, and a `limit` or an `enum` (with its `delimiter`). The reference is
`examples/packages/README.md`. Entries ride with roles and permissions: they are part of
`PackageDeclarations` (`Attributes`), planned by `PackageDeclarationPlanner` and written by
`IPackageDeclarationService` through the Mediator (`CreateAttributeEntryCommand`,
`DeleteAttributeEntryCommand`). The decisions:

- **Validity.** The manifest reader checks the name (`@attribute`'s characters, `` ` `` only between
  branches), each flag against the built-in attribute flags (`branch` refused), that a `limit`
  compiles as `@attribute/limit` compiles it, and that an entry has a limit or an enum, not both.
  `attributes:` needs `format: 1.5`. `index:` takes `true` or `@attribute/index`'s option words
  (`AttributeIndexOptions.Parse`), needs `format: 1.8`, and is refused with `internal` in the
  flags. Below 1.8 the key keeps its 1.5 reading, reserved and ignored, with a warning.
  `watch:` takes `true` (`AttributeWatch.DefaultPattern`) or `@attribute/watch`'s pattern, needs
  `format: 1.9`, and below it is ignored with a warning; `SetAttributeWatchCommand` applies it.
  The plan checks the live table and the game's attribute flags.
- **Ownership.** An entry the package creates is recorded in `InstalledPackageRecord.Owned` with the
  values that version declared. An entry the game already has is the game's: the package uses it
  and never changes it; if its limit or enum contradicts the manifest's, the plan says so and blocks
  (different default flags are only noted). One owned by another installed package blocks the plan.
- **Upgrade** is a field-wise three-way merge against `Owned`: the default flags as a set, the
  limit-or-enum as one field, since each replaces the other, and the index and the watch as one field each.
- **Index.** Applying an entry with an index writes it with `SetAttributeIndexCommand` and starts
  the background build (`IAttributeIndexBuilder`); the plan's change says how many objects it builds
  over (`CountAttributeCopiesQuery`). A game's own entry without the declared index blocks the plan,
  since the package's `indexed()` calls depend on its order and fold.
- **Removal** (a dropped entry, or uninstall) deletes the entry, whatever relies on it; stored values
  on objects are left alone, since an entry only sets defaults and checks.
- **Retroactive.** Default flags apply to new copies only, unless the entry sets `retroactive: true`
  and the apply writes its flags, or a version turns `retroactive` on. The plan counts the existing copies whose flags differ
  (`PackageDeclarationChange.Retroactive`), the review screen shows the count and waits for a
  confirm, and the apply gives each copy exactly the entry's flags through the operation's
  `PackageWriteTransaction` (so a failed apply undoes them), keeping `branch` and the copy's owner.
- **Rollback** restores objects and attributes only.

### Engine version range (format 1.6)

`requires_server` (lower-bound clauses only), `verified_server` (a version) and `maximum_server` (one to
three components; the ones left out match anything, `PackageVersionCeiling`) place a package against
the server's `EngineVersion`, registered from `VersionInfo.SharpMUSHVersion`, the version `version()`
reports. The reference is
`examples/packages/README.md`; the decisions:

- **Only the minimum blocks.** A server below `requires_server` blocks the plan
  (`PackageChangeset.Compatibility`, `IsBlocked`) and the apply. A server a major version past
  `verified_server` warns. A server past `maximum_server` warns and the apply is refused without
  `PackageApplyRequest.AllowPastMaximum`, the review screen's "Install it anyway".
- **The choice is recorded.** A confirmed apply past the maximum sets
  `InstalledPackageRecord.PastMaximum` (server version, maximum, time), shown on the Packages page,
  and the portal writes a `package.past-maximum` audit entry. An apply inside the range clears it.
- **Both apply paths gate.** The check runs before the managed (plugin, UI) branch, so a plugin
  package is held to the same range as a softcode one.
- **Bounds are for known breaks.** Bundled packages state no `maximum_server` and no dependency upper
  bound; the SharpMUSH.Docs guide "Versioning packages" says what counts as breaking.

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
