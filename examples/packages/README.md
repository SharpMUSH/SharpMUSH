# SharpMUSH Softcode Packages

A **package** is a declarative description of softcode: the objects that should
exist on a MUSH and the attributes, flags, locks, and parent relationships they
carry. Packages are installed, upgraded, and authored through the web admin
panel (`/admin/packages`) using a plan/apply model — the server computes a
changeset against the live database, a wizard reviews it, and only then is
anything written. Upgrades use three-way merge against stored baselines, every
apply is recorded as a revision, and installs can be rolled back.

This folder contains the manifest format reference (format **v1**, decisions
20.1–20.20 in `docs/design/architectural-decisions.md`) and validated example
packages. Every manifest here is parsed by the test suite
(`SharpMUSH.Tests/Packages/ExamplePackageTests.cs`), so the examples are
guaranteed to stay in sync with the parser.

These examples are also published as the seed content of the official package
repo, [SharpMUSH/SharpMUSH-Packages](https://github.com/SharpMUSH/SharpMUSH-Packages),
which games can add as a remote in the admin panel.

## Example packages

| Package | Demonstrates |
|---|---|
| [`hello-world/`](hello-world/) | The minimal manifest: one object, one command attribute |
| [`who-where/`](who-where/) | Multiple objects, internal refs, parents, flags, a convention prefix |
| [`starter-area/`](starter-area/) | Rooms and exits (`location:` / `destination:`) |
| [`bbs-lite/`](bbs-lite/) | Dependencies with source hints, typed configure params, cross-package refs, conflicts, locks, prerelease versions |

## The manifest: `package.yaml`

A package is a directory containing a `package.yaml` manifest. The manifest is
**desired state**, not a command script — there is no ordering, no `@create`
lines, and crucially **no dbrefs**. Object identity is expressed through
`{{ref}}` tokens that the install engine resolves to real dbrefs at apply time.

```yaml
format: 1                       # manifest format version (omit = 1)
package: myrddins-bbs           # required — id slug: lowercase, digits, hyphens, ≤64 chars
version: 2.4.1                  # required — semantic version
authors: [Myrddin]              # optional — list (or single string)
description: "Bulletin Board"   # optional
license: MIT                    # optional — SPDX expression recommended
homepage: https://example.com   # optional
keywords: [bbs, boards]         # optional — up to 5, feeds browse/search
convention_prefix: BBS_         # optional — advisory attr prefix, not enforced
requires_server: ">=0.1"        # optional — SharpMUSH version constraint

depends:                        # optional — other packages this one requires
  - volund-core: ">=1.0"        # shorthand: id + version constraint
  - some-util                   # bare id = any release version
  - package: who-where          # full form: adds a source hint (see below)
    version: ">=1.0 <2.0"
    source:
      repo: https://github.com/SharpMUSH/SharpMUSH-Packages
      path: who-where/
      branch: main

conflicts:                      # optional — cannot be installed alongside
  - legacy-bbs: "<2.0"

replaces: myrddins-board        # optional — id this package supersedes

configure:                      # optional — installer-supplied parameters
  bbs_storage:
    label: "Object that stores posts"
    type: dbref                 # dbref (default) | string | number | boolean
  board_name:
    label: "Board display name"
    type: string
    default: "Community Board"  # defaults allowed except for dbref type

objects:                        # required — at least one object
  - ref: bbs_global             # required — unique intra-package name (lowercase)
    type: thing                 # required — thing | room | exit | player
    name: BBS Global Object     # required — in-game name
    parent: "{{bbs_parent}}"    # optional — a single ref token
    location: "{{?bbs_storage}}"  # optional for things; required for exits
    previous_refs: [bbs_main]   # optional — retired names of this object (renames)
    flags: [no_command]         # optional — object flags
    locks:                      # optional — lock values by lock type
      use: "{{bbs_parent}}"
    attributes:                 # optional — attributes to set
      CMD_+BBREAD:              # full form: value + attribute flags
        value: |-
          $+bbread *:@pemit %#=[u({{bbs_parent}}/FN_READ,%0)]
        flags: []
      FN_HEADER: "[repeat(=,78)]"   # shorthand: quoted one-liners only

  - ref: bbs_parent
    type: thing
    name: BBS Parent Object
```

### Writing MUSHcode values: always block scalars

YAML mangles raw code: double quotes process `\` escapes (or hard-fail on
unknown ones), and *unquoted* values are worse — a leading `&CMD` is silently
eaten as a YAML anchor, a mid-value ` #` starts a comment and silently
truncates (`think #123` becomes `think`), and values starting with
`@ % [ { * ! | -` break outright.

**Write every MUSHcode value as a block scalar (`|-`).** Block scalars carry
backslashes, brackets, `$patterns`, `%subs`, and ` #dbrefs` byte-for-byte.
Single-quoted strings are acceptable for trivial one-liners (double any
embedded `'`). Never use plain or double-quoted style for code. Two block
scalar rules: indent with spaces (tabs are a YAML error) and use `|-` (strip)
unless you want a trailing newline.

### Refs: `{{...}}` tokens, never dbrefs

| Syntax | Kind | Resolved from |
|---|---|---|
| `{{name}}` | internal | Another object defined in this package |
| `{{pkg/name}}` | cross-package | An object owned by `pkg`, which must be listed under `depends:` |
| `{{$name}}` | well-known | Server configuration: `room_zero`, `master_room`, `player_start`, `god`, `package_manager` (servers may add more) |
| `{{?name}}` | configure | The installing admin, prompted during review; must be declared under `configure:` |

Refs appear in `parent:` / `location:` / `destination:` fields (exactly one
token, quoted — a bare `{` confuses YAML), and anywhere inside attribute and
lock values.

**How refs resolve at apply time (decision 20.21):** installed code never
contains raw dbrefs. In attribute values, each token becomes a
`` [v(PM`REFS`NAME)] `` recall, and the engine maintains a `` PM`REFS`NAME ``
attribute on every object whose code uses the ref, holding the resolved
objid (or configure value). Cross-package refs namespace by dependency id
(`` PM`REFS`WHO-WHERE`WW_FUNCTIONS ``). That keeps installed code readable,
lets admins re-point a ref by editing one attribute — and because the ref
attrs are baseline-managed, a local re-point survives upgrades as a kept
local change. Structural fields (`parent:`/`location:`/`destination:`) and
lock strings are not function-evaluated, so they resolve to dbrefs directly.
The `` PM` `` attribute tree is reserved: manifests may not define attributes
under it, and ref names must be unique across kinds within a package (an
object ref `storage` and a configure key `storage` would collide in
`` PM`REFS`STORAGE ``).

Rules, all enforced at parse time:

- Ref names are **case-insensitive** (`{{BBS_PARENT}}` ≡ `{{bbs_parent}}`);
  define them in lowercase.
- **Every `{{...}}` token must be a valid, resolvable ref.** Malformed bodies,
  unresolved internal refs, unknown well-known names, undeclared `{{?refs}}`,
  and `{{pkg/...}}` refs to packages not in `depends:` are all hard errors.
- Literal `{{` in code must be escaped by doubling: `{{{{` produces `{{`.
  (Nested MUSHcode brace groups like `@switch x={{a},{b}}` are fine as-is —
  only exactly-double braces around a simple token are parsed.)
- Only **dbref-typed** configure refs may be used in
  `parent:`/`location:`/`destination:`.

### Object placement

- **Exits** require `location:` (source room) and `destination:`.
- **Rooms** may not declare `location:` (and only exits get `destination:`).
- **Things/players** may declare `location:`; without one they land in the
  Package Manager wizard's inventory.

### Attach mode: managing attributes on an existing object

Some packages need to manage the *softcode on a core object they don't create*
— e.g. the configured HTTP handler, the master room. An object entry with a
`target:` ref is an **attach object**: the package manages only its declared
attributes and never creates, restructures, or destroys it.

```yaml
objects:
  - ref: handler
    target: "{{$http_handler}}"   # an existing {{$well_known}} or {{?configure}} object
    attributes:
      GET: |-
        think routed
```

- Only `ref`, `target`, and `attributes` are allowed — no
  `type`/`name`/`parent`/`location`/`destination`/`flags`/`locks`.
- The `target` must resolve to an object that already exists (the package can't
  create it); a `{{$well_known}}` or `{{?configure}}` ref.
- The package records the managed attributes (so upgrades three-way-merge and
  uninstall removes them) but **not** the object — uninstall leaves it in place.
- The `http-hooks` example is the canonical attach package.

### Versions and constraints

Versions are SemVer: `MAJOR[.MINOR[.PATCH]][-prerelease]` (`2.4.1`, `1.0`,
`3.0.0-beta.1`). Prerelease precedence follows SemVer 2.0.0 exactly:
`alpha < alpha.1 < alpha.beta < beta < beta.2 < beta.11 < rc.1 < release`.
Build metadata (`+...`) is not supported.

Dependency constraints support `>=`, `>`, `<=`, `<`, `=` and bare versions
(exact match). Space- or comma-separated clauses are ANDed. Caret/tilde
ranges (`^1.2`, `~1.2`) are **not** supported — write `>=1.2 <2.0`.

**Prereleases are never selected implicitly**: `>=1.0` does not match
`2.0.0-beta`. A prerelease only satisfies a constraint when some clause names
a prerelease on the same `major.minor.patch` tuple (`>=1.2.3-alpha` matches
`1.2.3-beta` but not `1.3.0-beta`).

A package cannot depend on itself, duplicate ids are rejected, and an id may
not appear in both `depends:` and `conflicts:`. `provides:`, `recommends:`,
and `suggests:` are reserved for a future format version.

#### Source hints — where to find a dependency

A dependency can say where it lives, so an unmet dependency becomes
"requires who-where >=1.0 — fetch it from this repo?" instead of a dead end.
Use the full mapping form (a `package:` key marks it; this also means
`package` itself is reserved as a dependency id):

```yaml
depends:
  - package: who-where
    version: ">=1.0 <2.0"       # optional; omit for any release version
    source:                     # optional
      repo: https://github.com/SharpMUSH/SharpMUSH-Packages
      path: who-where/          # optional — package directory in a monorepo
      branch: main              # optional — defaults to the remote default
```

`source:` also accepts a bare repo URL string. The hint is advisory: the
installer shows it (with the usual trust warnings for repos that aren't
configured remotes) and the admin decides whether to fetch from it. It never
overrides an already-configured remote for the same package.

### Renames

Objects: list retired ref names in `previous_refs:` so an upgrade treats the
change as a rename (same dbref) instead of destroy-and-recreate — on a live
MUSH, everything pointing at the old dbref would otherwise break. Packages:
`replaces: old-id` carries registry continuity across a package rename.

### Validation: errors vs. warnings

The parser (`PackageManifestService`) validates the whole document and reports
**every** issue with a precise path (e.g. `objects[2].attributes.FN_READ`).

Errors (manifest is rejected): missing/invalid `package`, `version`, or
`objects`; invalid slugs, ref names, or object types; duplicate refs,
attributes, or dependency ids; any invalid or unresolvable `{{token}}`;
parent cycles; exits without location/destination (or location on a room /
destination on a non-exit); configure type violations (dbref defaults,
non-dbref refs in placement fields); self/overlapping depends-conflicts;
format major newer than supported.

Warnings (manifest is accepted): unknown keys (typo detection); reserved keys
(`provides`, `recommends`, `suggests`, configure `pattern`); declared-but-
unused configure refs; more than 5 keywords; format minor newer than
supported.

## Repos, discovery, and releases

A git repo holds one or more packages. A repo root may carry an `index.yaml`
for fast discovery; without one, the repo is scanned for `package.yaml` files.
Index entries may carry `package`/`version`/`description` so browsers can
render without parsing every manifest (duplicate ids in an index are errors):

```yaml
name: SharpMUSH Packages
description: Official softcode packages.
packages:
  - path: who-where/
    package: who-where
    version: 1.2.0
    description: +who and +where commands
```

**Releases are git tags** named `<package-dir>/v<version>` — e.g.
`who-where/v1.2.0` (the Go-modules monorepo convention). The version list in
the browser is the tag list; installing a version checks out its tag; HEAD is
the development channel. Release tags are immutable: never move or delete one
— republish a fix as a new version. The installer records the commit it
applied and warns loudly if a tag it installed from no longer points there.

## Ownership and upgrade model

Installed objects are owned by the dedicated **Package Manager wizard**
(`#3`, configurable via the `package_manager` database option). The system
database tracks which package created which object and which package manages
which attribute — including the full as-installed baseline value of every
managed attribute, which powers three-way upgrades (base vs. your live edits
vs. the new version) with a real base pane. Every apply is recorded as a
revision (resolved manifest, your configure answers, and the pre-apply values
of everything it changed), so an install or upgrade can be rolled back, and
upgrades never re-ask configure questions you've already answered.

## Authoring tips

- Write MUSHcode substitutions bare: `%0`, `%q<name>` — brackets are for
  function calls only.
- Prefer `firstof()` over nested `if()` chains for priority fallbacks.
- Pick a `convention_prefix` and use it on attribute names; it is advisory.
- Keep function objects `no_command` and put `$command` patterns on a separate
  global object — see `who-where/` for the shape.
- Never hardcode a dbref. Use `{{?configure}}` for game-specific objects and
  `{{$well_known}}` for universal ones.
- Quote ref-only fields (`parent: "{{x}}"`) — a bare leading `{` is YAML flow
  syntax.
