# Package Format Review — Findings & Proposed Revisions

**Status: DECIDED 2026-06-12** — adopted as decisions 20.11–20.20 in
`architectural-decisions.md`, with two owner amendments to the
recommendations: ref syntax is uniform mustache `{{...}}` (not braced
sigils `~{}`), and the code carrier is block-scalars-only (no
`value_from` external files). Configure parameters are typed in v1
(the more ambitious option). This document remains as the evidence record.

Produced 2026-06-12 from (a) empirical probes
against the Phase 1 parser (`/tmp/pkgprobe`, results reproduced below) and (b) web
research into crates.io, Go modules, Nix flakes, Homebrew taps, Terraform modules,
dpkg/ucf/RPM/portage config handling, Helm 2→3, npm/Cargo manifest conventions,
SemVer 2.0.0, and node-semver. Each finding cites prior art. Items marked **[Dx]**
are proposed decisions 20.11–20.20 for `architectural-decisions.md` once confirmed.

The format is one day old with zero third-party packages in the wild. Every breaking
change below is free today and expensive later.

---

## 1. CRITICAL — The ref sigils are not special enough

`~name`, `$name`, `?name` all collide with MUSHcode syntax. Confirmed empirically
against the current parser:

| Input in an attribute value | What happens | Class |
|---|---|---|
| `type ~help for assistance`, `~~~wave~~~` | Hard error: "'~help' does not match any object ref" — **install-blocking false positive on prose** | False positive |
| `$god *:@pemit %#=...` (command pattern; `god` is in the well-known set) | Parses **silently**; apply would substitute a dbref into the command pattern | Silent corruption |
| `switch(%0, ?board, ...)` glob, with `board` declared under `configure:` | Parses silently; apply would substitute the wildcard | Silent corruption |
| `%?board` (`%?` substitution + text) | Same silent substitution of `?board` | Silent corruption |
| `U(~BBS_PARENT/FN)` vs ref `bbs_parent` | Hard error — scanner is case-insensitive, resolution is case-sensitive; MUSHcode is conventionally uppercase | Inconsistency |
| Any literal `~ref`-shaped text you actually want | No escape syntax exists | Missing escape |

### Proposed fix [D-20.11]: brace-delimited refs, case-insensitive, with escapes

```
~{bbs_parent}        intra-package
~{who-where/ww_functions}   cross-package (target must be in depends — see §5)
${room_zero}         well-known
?{game_config}       configure
```

- The digraphs `~{`, `${`, `?{` effectively never occur in MUSHcode (`$` command
  patterns are `$text:`; `{}` grouping follows `(`/`,`/`=`, not these sigils).
- Unambiguous boundaries mean: names may contain `-` and `/`, scanning has no
  false positives, and **every** unresolved/undeclared ref can be a hard error —
  no more heuristic warnings for `?name`.
- Case-insensitive end-to-end (define refs lowercase; match and resolve any case),
  because MUSHcode is case-insensitive and authors write uppercase.
- Escape by doubling the sigil: `~~{` renders a literal `~{` (same for `$${`, `??{`).
- `parent:` / `location:` / `destination:` fields accept the same braced forms only.

This also unlocks **cross-package references**, which the current format cannot
express at all — despite the design doc's own primary monorepo use-case
(volund-bbs putting `CMD_+BBADMIN` on volund-core's object) requiring them.

---

## 2. CRITICAL — Upgrade tracking needs full baselines and revisions, not hashes

The design stores `baseline_hash` in `sys_managed_attributes`. Research verdict on
hash-only baselines (dpkg conffiles, RPM `%config`): they can *classify*
(changed/unchanged) but **cannot render a base pane, cannot diff user changes,
cannot auto-merge, cannot roll back**. dpkg's `D` option is two-way only; Debian
created `ucf` specifically to fix this by caching full base copies
(`/var/lib/ucf/cache`), enabling real `diff3 -m` three-way merges with preview.
Gentoo `dispatch-conf` does the same (`/etc/config-archive`, `diff3 -mE`).
Helm 3 went furthest: it stores the **complete rendered manifest of every release
revision** (Secret `sh.helm.release.v1.<name>.v<N>`), which is precisely what
enables its three-way patches *and* `helm rollback` (rollback creates a new
revision; default retention 10).

Our own UX doc already promises a three-pane Base/Live/New view — unrenderable
from a hash.

### Proposed fix [D-20.13]

1. `sys_managed_attributes`: store `baseline_value` (full text) alongside
   `baseline_hash` (hash stays as the cheap drift-detection index). Attribute
   values are small text; cost is negligible.
2. New `sys_package_revisions`: one record per apply (install/upgrade/rollback) —
   package, monotonic revision number, the **fully resolved manifest snapshot**
   (refs→dbrefs, configure answers, per-conflict resolutions), source commit,
   version, timestamp, and the **pre-apply values** of everything modified
   (the `.dpkg-old` analog).
3. Rollback = apply the previous revision's snapshot as a new revision
   (Helm semantics). Retention configurable (default ~10).
4. Persisted configure answers mean upgrades don't re-prompt.
5. Drift detection ("N locally modified attrs" badge): compare live hash vs
   baseline hash; render diffs from baseline value.
6. Realism note: MUSHcode attr values are often single long lines, so line-based
   diff3 auto-merge will frequently degrade to "conflict" — auto-resolve only the
   safe dpkg-matrix cases, send true conflicts to the human. The stored base still
   powers the three-pane view either way.

### The three-way table is incomplete [D-20.15]

The design's truth table covers value modification only. The plan engine must also
classify:

- **Delete**: attr present in base, absent in new → delete (if live == base) or
  **modify/delete conflict** (if live ≠ base). Same at object level (object
  removed from package → propose @destroy, with contents/exit handling).
- **Add/add conflict**: attr new in package but already exists live with a
  different value (not "create" — it would clobber local work).
- **Renames**: a renamed ref must not become destroy+create — that changes the
  dbref and breaks everything pointing at it on a live MUSH. Add per-object
  `previous_refs: [old_name]` and package-level `replaces: old-package-id`
  (Debian `Replaces`; Homebrew records cross-tap moves in `tap_migrations.json`).

---

## 3. CRITICAL — Versioned releases need git tags, not just HEAD

A repo's HEAD holds exactly one version of each package; the UX promises "version
list" and installing/pinning specific versions. Scanning history for version
changes is fragile. Prior art: **Go modules** resolve versions purely from VCS
tags, and the multi-module monorepo convention is exactly our shape — tag
`<subdir>/v<semver>` (e.g. `gopls/v0.4.0` in golang/tools).

### Proposed fix [D-20.14]

- Releases are annotated tags named `<package-dir>/v<version>` (e.g.
  `who-where/v1.2.0`). Version list = tag list; install = checkout tag; upgrade
  check = newest tag vs installed (the `git diff --name-only` path-change check
  remains as a cheap "something changed at HEAD" signal / dev-channel indicator).
- Untagged repos remain installable at HEAD only (single-version mode).
- **Immutability**: document and CI-enforce in SharpMUSH-Packages that release
  tags never move and released content is never rewritten (crates.io: "a publish
  is permanent. The version can never be overwritten"; Go's sumdb turns moved
  tags into `SECURITY ERROR: checksum mismatch`).
- Consumer-side tag-move detection (our lightweight go.sum): we already record
  `installed_commit`; on upgrade, if tag `<pkg>/v<installed_version>` exists but
  no longer points at `installed_commit` → loud trust warning before showing the
  changeset. Terraform is the cautionary tale here — no content checksums for
  git-sourced modules, silently follows moved refs.

---

## 4. HIGH — Embedding MUSHcode in YAML is a minefield; standardize the carrier

Verified (YamlDotNet 16.3.0, matching upstream spec):

- **Double quotes**: `\` escapes processed — `"\n"` silently becomes a newline;
  any unknown escape (`\[`, `\%`, `\q`) is a **hard parse error**. MUSHcode is
  full of backslashes.
- **Plain (unquoted) scalars**: values starting with `@`, `%`, `[`, `{`, `&`,
  `*`, `!`, `|`, `-`, `?`, `:` either explode or silently change meaning —
  `&CMD_X foo` is **silently eaten as a YAML anchor**; mid-value ` #` starts a
  comment and **silently truncates** (`think #123 is here` → `think`); `: ` is a
  parse error. Dbref-laden softcode triggers these constantly.
- **Block scalars (`|-`)**: round-trip MUSHcode byte-perfect (verified with
  `\[`, `[ansi(...)]`, `$cmd *:`, `%#=`). Pitfalls are manageable: no tabs in
  indentation, `|-` vs `|` chomping.
- Good news, also verified: YamlDotNet untyped deserialization returns **all
  scalars as strings** (no Norway problem, `version: 1.0` is safe in *our*
  parser) — but other tooling reading the same files (linters, CI in other
  languages, editors) applies YAML 1.1/1.2 typing, so quoting discipline still
  matters for the ecosystem.

### Proposed fix [D-20.12]

1. Docs and examples mandate `|-` block scalars (or single quotes for trivial
   one-liners); the authoring exporter emits block scalars only. Our current
   examples model double-quoted style — rewrite them.
2. Add `value_from: <relative path>` referencing a sibling file (e.g.
   `objects/bbs_global/FN_READ.mush`) as the preferred carrier for real code.
   This is the universal "manifest = metadata, payload = sibling files" pattern
   (Ansible roles, Helm `templates/`, conda-build scripts, Debian maintainer
   scripts). Wins: zero YAML escaping; per-attribute git diffs (which makes the
   `git diff` update-detection and review UIs dramatically better); editor
   highlighting for `.mush` files; hand-editability.
3. Parser hardening: when an attribute deserializes as a non-string (YAML ate
   it), the error should explain the likely cause and suggest `|-`.

---

## 5. HIGH — Identity, naming, and collisions [D-20.18]

- **Flat id namespace across remotes**: two remotes can both publish `bbs`.
  Rule: installed package identity = id **bound to its source repo**
  (`sys_packages.source_repo`, already designed). Installing id X from a
  different repo than the recorded one is a hard error with guidance (Homebrew:
  core wins, otherwise fully-qualified `user/repo/formula` required; npm solved
  the same problem with scopes).
- **Moniker/typosquat rule** (npm, post-crossenv 2017): within one repo/index,
  reject a new package whose name collapses to an existing one when punctuation
  is stripped (`who-where` vs `whowhere`). Enforce in SharpMUSH-Packages CI.
- **Name limits**: add a max length (crates.io: 64 ASCII chars). Reserve
  `package` (already structurally reserved by the full dependency form), plus
  a short reserved list for future syntax.
- Trust levels stay **server-side per-remote configuration**, never
  self-declared in repo metadata — current design already correct; keep it.

---

## 6. HIGH — Manifest gaps

1. **`format: 1`** [D-20.17] — a manifest format version field, the one thing
   every surviving ecosystem has (Kubernetes `apiVersion`; Python
   `Metadata-Version`, whose rule we should copy: unknown **minor** → warn and
   proceed, unknown **major** → reject; Cargo `edition` with missing = oldest).
   Missing = `1`. Costless now, impossible to retrofit cleanly later.
2. **Exits are currently inexpressible** — `type: exit` exists but there's no
   source room or destination. Add `location: <ref>` (required for exits,
   optional things; default for things = PM wizard inventory) and
   `destination: <ref>` (exits only). Rooms: neither.
3. **Engine compatibility**: `requires_server: ">=X.Y"` (Cargo `rust-version`
   precedent — bare minimum version, checked at plan time, shown in browse).
   Softcode silently breaking on missing engine functions is otherwise
   undiagnosable at install time.
4. **Metadata for browse/search & licensing**: `license` (crates.io requires
   one to publish; MU* softcode has a real history of usage terms),
   `homepage`/`repository`, `keywords` (≤5, like crates), `category`. The
   browse/search UI in the UX doc has nothing to search on today but name and
   description.
5. **Dependency vocabulary** [D-20.20]: add `conflicts: [other-package]`
   (Debian `Conflicts`) — and, better, the plan engine should auto-detect
   `$command`-pattern collisions across installed packages (the MUSH analog of
   file conflicts; dpkg refuses file overlap). Reserve `provides:` (virtual
   packages, e.g. any-BBS) and `recommends:`/`suggests:` for v2 — parse and
   ignore-with-warning now so old parsers don't choke later.
6. **Configure refs** [D-20.19]: currently dbref-only. Keep for v1; reserve
   typed parameters (`type: dbref|string|number|boolean`, `default:` — debconf
   precedent) for v2. Validate supplied dbrefs exist at apply. Persist answers
   in revisions (§2).
7. **Runtime-data residue**: bbs-lite writes `POST_*` attrs onto the
   `?{bbs_storage}` object at runtime — invisible to `sys_managed_attributes`,
   so uninstall leaves them. Add optional `cleanup:` patterns per configure ref
   (`?{bbs_storage}: [POST_*]`) feeding the uninstall preview; document the
   stance either way.
8. **Attribute trees**: PennMUSH backtick branches (`` FOO`BAR ``) — apply
   engine must auto-create parent attrs; name validation should explicitly
   allow internal backticks, forbid leading/trailing.
9. **index.yaml is path-only**: add optional `package:` (id), `version:`,
   `description:` per entry → browse renders without parsing every manifest,
   and CI can detect duplicate ids and index/manifest drift (scaled-down
   crates.io index lesson; their per-version JSON lines carry name/vers/deps/
   cksum/yanked).

---

## 7. MEDIUM — Version & constraint semantics

1. **Prerelease ordering bug (confirmed)**: current `string.CompareOrdinal`
   violates SemVer item 11 — spec requires dot-split identifiers, numeric
   identifiers compared numerically and ALWAYS lower than alphanumeric, fewer
   fields < more when prefix equal. Spec chain that must hold:
   `1.0.0-alpha < 1.0.0-alpha.1 < 1.0.0-alpha.beta < 1.0.0-beta < 1.0.0-beta.2
   < 1.0.0-beta.11 < 1.0.0-rc.1 < 1.0.0`. Ours sorts `beta.11 < beta.2`. Fix
   `PackageVersion.CompareTo`.
2. **Prerelease range matching** [D-20.16]: `>=1.0` currently matches
   `2.0.0-beta`. Adopt node-semver's rule: a prerelease version satisfies a
   range only if some comparator with the **same [major,minor,patch]** carries a
   prerelease (rationale: prereleases are never pulled in implicitly; opting
   into `1.2.3-alpha` opts into that line only).
3. **No `^`/`~` operators — keep it that way**, but special-case the error
   message: `^1.2` / `~1.2` → "caret/tilde ranges are not supported; write
   `>=1.2 <2.0`" (`~` is doubly confusing next to our ref sigil).
4. Build metadata (`+build`) unsupported — fine; say so in the parse error.

---

## 8. Repo structure standard & examples quality

Standard package directory (after revisions):

```
who-where/
├── package.yaml      # metadata + small inline values (block scalars)
├── README.md
├── CHANGELOG.md      # surfaced in the upgrade review screen
├── LICENSE
└── objects/          # value_from payloads
    └── ww_functions/
        └── WW_FN_HEADER.mush
```

SharpMUSH-Packages repo: add CI that runs our parser over index + manifests,
enforces the moniker rule, version-bump-on-content-change, and tag protection.

**Example gaps to close once the format revisions land**: all current examples
model double-quoted one-liners (the style our own research condemns) — rewrite
with `|-`/`value_from`; add a rooms-and-exits area example (blocked on §6.2);
a two-package monorepo with a cross-package parent (blocked on §1) and
per-package tags; LICENSE/CHANGELOG files; a `conflicts:` demonstration.

---

## 9. Code-level fixes already identified (cheap, do with format v2 pass)

- Ref case-insensitivity end-to-end (§1).
- `PackageVersion.CompareTo` per SemVer item 11 (§7.1); constraint prerelease
  rule (§7.2); friendlier `^`/`~`/`+` errors (§7.3–7.4).
- Non-string attribute value error message explains YAML plain-scalar traps (§4.3).
- WellKnownRefs becomes server-extensible (config-driven) rather than a fixed set.

## Proposed decision register (for architectural-decisions.md once confirmed)

- **20.11** Brace-delimited case-insensitive refs `~{}`/`${}`/`?{}`, cross-package
  `~{pkg/ref}` (target must be a declared dependency), doubling escape, all
  unresolved refs are errors.
- **20.12** MUSHcode carrier: block scalars or `value_from` sibling files;
  exporter never emits plain/double-quoted code.
- **20.13** Full-value baselines + per-apply revision snapshots (resolved
  manifest, configure answers, pre-apply values); rollback = new revision.
- **20.14** Releases via `<pkg>/v<version>` git tags; tag immutability +
  consumer-side moved-tag detection; HEAD = dev channel.
- **20.15** Extended changeset classification (delete, add/add, modify/delete
  conflicts) + rename support (`previous_refs`, `replaces`).
- **20.16** SemVer-correct prerelease ordering; node-semver prerelease range
  rule; no caret/tilde.
- **20.17** `format:` field (missing=1; minor-warn/major-reject), `license`,
  `homepage`, `keywords`, `requires_server`; exits get `location`/`destination`.
- **20.18** Package identity = id ⊕ source repo; cross-source install is an
  error; moniker rule + 64-char ids in repo CI.
- **20.19** Configure: declared-only, dbref-typed v1; typed/defaulted v2;
  answers persisted.
- **20.20** `conflicts:` honored, `provides:`/`recommends:` reserved;
  plan-engine `$command`-collision detection.
