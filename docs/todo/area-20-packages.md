# Area 20: Softcode Package Manager — TODO

## Pre-Implementation
- [x] Review & confirm decisions (20.1–20.x) with project owner
      (20.1–20.10 affirmed by building on them; 20.11–20.20 explicitly confirmed 2026-06-12)
- [x] Identify any decisions that need revision based on current codebase state
      → see `docs/design/softcode-package-manager-format-review.md` (2026-06-12):
      proposed decisions 20.11–20.20 (ref syntax v2, full-value baselines +
      revisions, tag-based releases, format versioning, etc.)
- [x] Confirm/reject proposed decisions 20.11–20.20 with project owner
      (confirmed 2026-06-12; owner amendments: uniform mustache `{{...}}` refs,
      block-scalars-only carrier, typed configure params in v1 — recorded in
      architectural-decisions.md)
- [x] Apply format v2 revisions to parser, models, examples, and SharpMUSH-Packages repo
      (mustache scanner + escape, cross-package refs, exits location/destination,
      format/license/keywords/requires_server, conflicts/replaces/previous_refs,
      typed configure, SemVer item-11 ordering + prerelease range rule,
      starter-area example, tools/PackageValidator, repo retagged `<pkg>/vX.Y.Z`
      with CI + tag-immutability ruleset)

## Implementation Tasks

### Phase 1: Package Format & Parsing
- [x] Define package.yaml schema (model records + code validation with tests; `SharpMUSH.Library/Models/Packages/`)
- [x] YAML parser for package manifests (`PackageManifestService`, also parses index.yaml)
- [x] Validate intra-package ~refs resolve
- [x] Validate dependency declarations parse correctly (semver + constraint parsing)
- [x] Handle `$well-known` and `?configure` ref types in manifest (`PackageRefScanner`; undeclared `?` tokens warn, `$` names validated against `WellKnownRefs`)
- [x] Format reference docs + validated example packages (`examples/packages/` — README, index.yaml, hello-world/who-where/bbs-lite; kept honest by `ExamplePackageTests`)

### Phase 2: System Database Collections
- [x] sys_packages collection (id, version, source_repo, source_path, installed_commit, installed_at, pinned_branch, current_revision)
- [x] sys_package_objects collection (package, ref, objid, type)
- [x] sys_package_depends edge collection (_from, _to, constraint)
- [x] sys_managed_attributes collection (package, objid, attr, baseline_value + baseline_hash, baseline_version — full values per decision 20.13)
- [x] sys_remotes collection (name, url, trust, branch)
- [x] sys_package_revisions collection (per-apply snapshots: resolved manifest, configure answers, pre-apply values — decision 20.13)
- [x] Implement across all three backends (Arango, Memgraph, SurrealDB) via `IPackageRegistryService`
      on each provider partial; Arango integration-tested (`PackageRegistryTests`, 6 tests);
      Memgraph/Surreal follow the providers' existing query patterns
- [x] PM wizard player (#3) already exists in all three backend migrations (`DatabaseOptions.PackageManager`)

### Phase 3: Plan Engine (Changeset Computation)
- [x] Read live DB state for objects/attrs referenced by package
      (`PackageInstallService.GatherInputsAsync`: registry records + live object
      names/attrs/contents + well-known/configure/cross-package resolution maps)
- [x] Compare desired state (manifest) vs. live state (`PackagePlanService`, pure/unit-tested)
- [x] Classify each change: create, modify, no-change — plus 20.15 extensions:
      delete, remove-baseline, adopt, rename (previous_refs, keeps dbref),
      recreate-missing, update-metadata
- [x] For upgrades: three-way compare (baseline, live, new) — full truth table incl.
      modify/modify, modify/delete, delete/modify, add/add conflicts with all three panes
- [x] Produce changeset data structure (`PackageChangeset` — objects, attrs, conflicts,
      dependency issues, $command collisions per 20.20, notes)
- [x] Dependency check: verify all deps met before planning (constraint check incl.
      prerelease rules; unmet deps carry source hints; conflicts: match prereleases too)

### Phase 4: Apply Engine
- [x] Create objects (rooms/things/exits; dbrefs recorded in sys_package_objects;
      exits split `Name;alias;...` and link source→destination; player objects
      rejected with a clear error for now)
- [x] Set attributes (final `{{ref}}` substitution via PackageRefSubstitution,
      including `{{{{` escapes; per-changeset-action semantics incl. conflict
      decisions KeepMine/TakeTheirs/UseCustom with dpkg baseline-advance rules)
- [x] Set flags, parents, locks on created objects (unknown flags noted+skipped;
      lock values ref-substituted) — fixed a pre-existing ArangoDB SetLockAsync
      duplicate-key crash on lock overwrite along the way
- [x] Set owner = PM wizard on all created objects (config `package_manager`, #3)
- [x] Update sys_managed_attributes with baseline VALUES + hashes (decision 20.13)
- [x] Update sys_packages with version + commit (+ source repo/path/branch, revision)
- [x] Handle `{{?configure}}` refs: answers supplied via PackageApplyRequest,
      defaults honored, substituted at apply; plan is re-runnable as answers arrive
- [x] Revisions: per-apply snapshot (objects, final attr values, configure answers,
      pre-apply undo data) + pruning; rollback applies an old snapshot as a NEW
      revision (`RollbackAsync`)
- [x] Uninstall: dependents block (unless forced), created objects marked GOING
      (@destroy convention), cross-package managed attrs cleared, registry cascade
- [x] End-to-end integration test: install → customize → conflicted upgrade →
      take-theirs → rollback → uninstall (`PackageInstallServiceTests`)

### Phase 5: Git Integration
- [x] Clone remote to temp cache on browse (`GitPackageSourceService` in
      SharpMUSH.Server — LibGit2Sharp; per-URL cache dirs under
      `$TMP/sharpmush-packages/<sha16>`; per-remote semaphore)
- [x] Pull existing cache on subsequent browse (fetch with forced tag refspec
      `+refs/tags/*:refs/tags/*` + TagFetchMode.None so MOVED tags become
      visible for the 20.14 trust check; never background-polled)
- [x] Scan for package.yaml files (or read index.yaml) — index fast-path with
      tree-scan fallback; all reads from commit trees, never the working copy
- [x] Tree-diff between installed_commit and branch tip filtered by package
      path for dev-channel update detection (sibling packages don't trip it)
- [x] Tag-based version listing per decision 20.14: `<pkg>/v<semver>` (bare
      `v<semver>` for root packages), newest first; manifest reads pinned to
      tag commits; moved-tag detection vs installed_commit
- [x] Branch pinning per remote (origin/<branch>, fallback origin/HEAD)
- [ ] Push support for authoring (auth: SSH key or token) — deferred to
      Phase 7 (authoring UI)

### Phase 6: Admin Panel — Consumer UI
- [x] Community repo directory ("ping the official repo for accepted community repos"):
      curated `community/*.yaml` listings in SharpMUSH-Packages (one file per repo,
      added by PR; `_`/`.` prefixed files ignored; CI-validated incl. duplicate URLs);
      `ParseCommunityListing` (unknown keys tolerated for forward compat);
      `GetCommunityListingsAsync`/`GetReadmeAsync` on IPackageSourceService;
      `PackagesController` first slice — GET /api/packages/community (aggregates all
      official remotes, falls back to canonical repo, dedupes, flags already-configured),
      GET /api/packages/community/readme (accepted-or-configured URLs ONLY — no
      arbitrary clones) and GET /api/packages/remotes/{name}/readme (root/package/
      tagged), rendered via WikiMarkdigPipeline (raw HTML stripped)
- [x] REST API complete (`PackagesController`): installed dashboard + revisions +
      rollback + uninstall (409 on dependents) + update-check; remotes CRUD;
      browse (refresh snapshot); plan (review payload w/ highlighted panes,
      configure prompts, danger flags); apply (explicit confirmation, decisions)
- [x] /admin/packages — status dashboard (installed, versions, update/moved-tag
      badges, revision history + rollback dialog, uninstall confirm w/ force)
- [x] /admin/packages/remotes — manage remotes (add/remove/trust) + community
      directory with READMEs rendered in-dialog
- [x] /admin/packages/browse — remote selector, package cards, version dropdown
      (tags + dev channel), READMEs, unknown-source warning banner
- [x] /admin/packages/review — changeset review screen (blockers, manifest
      warnings, $cmd collisions, notes, configure inputs w/ re-plan, object table)
- [x] Three-pane conflict view (base / live / new, highlighted)
- [x] Per-attr action selector (keep mine / take theirs / edit merged value)
- [x] Dangerous pattern scanner + visual flagging (`MushcodeHighlighter.FindDangerousPatterns` + chips/inline tint)
- [x] MUSHcode syntax highlighting (attr value display) — see Phase 8
- [x] Trust badges on source repos (`TrustBadge` component, official/community/unknown)
- [ ] Dependency visualization (graph view — deps shown as blockers/text today)
- [x] Uninstall preview + confirm flow (object/attr counts, dependents warning, force)

### Phase 7: Admin Panel — Authoring UI
- [x] Backend: `IPackageAuthoringService` — scan (objects, attrs, flags, parents,
      external-dbref report w/ occurrence counts) + export (dbref→{{ref}} for
      in-selection, {{$wk}}/{{?cfg}} for classified; literal {{ escaped; block
      scalars; round-trip validated through the parser; FAILS on any
      unclassified dbref); endpoints POST author/scan + author/export (yaml download)
- [x] /admin/packages/author — authoring page (objid-list picker + scan; search/zone
      filter picker is future polish)
- [x] Dbref scanner (find all #\d+ in attr values)
- [x] Auto-classify in-selection refs (dbref is in selection → {{ref}})
- [x] Batch resolution UI (classify unknowns as $well-known or ?configure, per-dbref
      with occurrence counts and example locations)
- [x] Per-object attribute selection UI (include/exclude checkboxes)
- [x] Metadata editor UI (id/version/description/license/authors)
- [x] Manifest validation (all refs resolve, no unclassified dbrefs)
- [x] Export: generate package.yaml
- [x] Export: download as file (in-dialog preview + data-URI download)
- [ ] Export: push to remote — DEFERRED to v2 (needs git credential management design)

### Phase 8: MUSHcode Rendering
- [x] Tokenizer for MUSHcode (functions, substitutions incl. %q<name>, $patterns,
      @commands, dbrefs, {{refs}}) — `MushcodeHighlighter`
- [x] Syntax highlighting renderer (HTML spans, mush-* CSS classes in custom.css;
      HTML-encoded output safe for MarkupString)
- [x] Dangerous pattern detection (@force, @toad, @newpassword, @nuke, @boot,
      @pcreate, @halt, @wall, @shutdown, @chown, @power, pemit(*) — advisory
      flags surfaced as chips + inline tint in review panes
- [ ] Dbref linking (clickable, shows resolution tooltip)
- [ ] Diff rendering (inline, highlight changes between versions — three-pane
      view covers review today)

### Phase 9: Default Packages (SharpMUSH Official Repo)
- [x] Create sharpmush-packages repo structure (github.com/SharpMUSH/SharpMUSH-Packages:
      index.yaml, community/ directory, CI validation, tag-immutability ruleset,
      seeded with hello-world / who-where / starter-area / bbs-lite)
- [ ] scene-system package — GATED on scene softcode hooks (area 7)
- [ ] bboard package — GATED on BBS engine work (area 16); bbs-lite is the stand-in
- [x] who-where package (+who, +where) — published and tagged who-where/v1.2.0
- [ ] events package — GATED on events system (area 17)
- [ ] finger package — straightforward once profile attrs settle (area 6)
- [x] http-handler + profile-handler packages — DONE. The default HTTP verb
      routers (`http-handler`) and the read-only profile/character-directory API
      (`profile-handler`, which depends on http-handler) — split attach-mode
      packages (`examples/packages/`, published + tagged http-handler/v1.0.0 and
      profile-handler/v1.0.0). Replace the hardcoded DefaultHttpVerbSoftcode/
      DefaultProfileHandlerSoftcode C# seeding; the bootstrap installs both
      bundled packages in dependency order at first boot. Proof of concept for
      the package manager owning a core system's softcode, with enable/disable
      granularity (verb routers without the profile API).
- [x] Each published package: manifest, README, validated in CI; install verified by
      the apply-engine e2e suite

## Testing (94+ tests across the area)
- [x] Package manifest: valid YAML parses, invalid rejected with clear errors
      (`PackageManifestServiceTests`, 58 tests incl. paths on every issue)
- [x] Plan engine: correct classification (`PackagePlanServiceTests`, 25 tests)
- [x] Three-way merge: full truth table incl. delete/add-add extensions (20.15)
- [x] Apply engine: objects created with PM-wizard owner, attrs set, refs resolved
      (`PackageInstallServiceTests` e2e: install→conflict→upgrade→rollback→uninstall)
- [x] Git integration: clone, fetch, tag versions, update detection, branch pinning,
      moved-tag trust check (`GitPackageSourceServiceTests` vs local fixture repo)
- [x] Dependency resolution: unmet deps block w/ source hints; conflicts match prereleases
- [x] Uninstall: objects marked GOING, attrs/records cleaned, dependents block
- [x] Cross-package attrs: registry queries (`PackageRegistryTests`) + cross-package
      parent ref in apply (bbs-lite pattern, authoring tests)
- [x] Dangerous pattern scanner: all listed patterns (`MushcodeHighlighterTests`)
- [x] Authoring: in-selection dbrefs auto-classify to {{refs}}; unclassified fail loudly
- [x] Full round-trip: author → export → install → verify state
      (`FullRoundTrip_AuthorExportInstall_VerifyState`)

### Iteration: Ref Indirection (decision 20.21)
- [x] Installed code recalls refs via `[v(PM`REFS`NAME)]` instead of hard-coded
      dbrefs; engine maintains baseline-managed `PM`REFS`*` attribute trees per
      object (user re-points survive upgrades as KeepLocal — tested)
- [x] Reserved `PM`` attribute tree (manifest validation + authoring export exclusion)
- [x] Cross-kind ref-name collision validation (shared PM`REFS namespace)
- [x] Structural fields and locks keep direct dbref resolution (not function-evaluated)
- [x] Registry + install e2e suites verified on SurrealDB and Memgraph providers
      (`SHARPMUSH_DATABASE_PROVIDER=surrealdb|memgraph`)

### Iteration: Cross-package attach + split default packages (decision 20.3)
- [x] Attach `target:` now also accepts `{{dependency/ref}}` (cross-package) —
      a package manages attributes on an object another package provides
- [x] Uninstall guard: a package that provides an object cannot be uninstalled
      while another package is attached to it (manages attributes on it),
      unless forced — complements the existing dependents block
- [x] Split http-hooks into `http-handler` (verb routers) + `profile-handler`
      (read-only directory/profile API, requires http-handler); the bootstrap
      installs both in dependency order; enables independent enable/disable
- [x] Tests: cross-package attach target parse test; cross-package attach
      install + provider-uninstall-blocked-while-attached integration test
      (verified on all three backends); split-package live integration test
- [x] Fixed a pre-existing GitPackageSourceServiceTests isolation bug
      (shared fixture repo + `.Single()` assumption — CI ordering exposed it)

### Iteration: Attach mode + http-hooks proof of concept (decision 20.3)
- [x] Manifest `target:` (attach) objects — manage attributes on an existing
      well-known/configure object without creating, restructuring, or destroying it
- [x] `http_handler` well-known ref (resolved from the http_handler config option)
- [x] Plan/apply/uninstall: attach objects record managed attributes but not
      ownership; uninstall clears attrs and leaves the object in place
- [x] `http-hooks` package authored (single source of truth for default handler
      softcode); `DefaultHttpHandlerBootstrapService` rewritten to install the
      bundled package; old hardcoded softcode C# files deleted
- [x] Tests: attach-mode parse/plan unit tests; isolated attach apply/uninstall
      integration test; live "handler is package-managed" + endpoint-serves tests;
      all existing HTTP/profile integration tests still pass (behavior identical),
      verified on ArangoDB + SurrealDB + Memgraph

### Iteration: cross-package attach + provider/attacher integrity (decision 20.3)
- [x] Attach `target:` may be `{{dependency/ref}}` (cross-package) in addition
      to `{{$well_known}}`/`{{?configure}}` — a package can manage attributes on
      an object ANOTHER package provides (the design doc's cross-package attribute
      ownership). Same-package internal targets rejected; cross-package requires
      a declared dependency
- [x] Uninstall guard: a package that PROVIDES an object cannot be uninstalled
      while another package is ATTACHED to it (manages attributes on it), unless
      forced — complements the existing dependents block
- [x] http-hooks split into http-handler (verb routers) + profile-handler
      (profile API, depends on http-handler); enable/disable each independently
- [x] Tests: cross-package attach parse/apply, provider-uninstall-blocked-while-
      attached (all 3 backends); split-package bootstrap + integration
- [x] Fixed pre-existing GitPackageSourceServiceTests shared-fixture ordering
      flake (.Single() assumed one package; now selects who-where by path)

### Iteration: Application packages (decision 20.22)
- [x] Manifest `kind:` discriminator — `softcode` (default) or `application`;
      format minor bumped to 1.1. Softcode requires `objects:`/forbids
      `application:`; application requires `application:`/forbids `objects:`
      (parser + validation, `PackageManifestService`)
- [x] `application:` block parses into `PackageApplicationSpec` (mirrors
      `RegisteredApplication`); string fields accept `{{?configure}}`/
      `{{$well_known}}`/`{{dependency/ref}}` refs (validated like attr values,
      substituted at apply)
- [x] Apply registers the application via `IApplicationRegistryService`,
      stamping `RegisteredApplication.OwningPackage`; uninstall reclaims every
      application a package owns. Plan surfaces the registration as a note
- [x] `OwningPackage` round-trips on all three providers (Arango/Memgraph/
      Surreal); `ApplicationsController` preserves it on manual edits and
      manual registrations stay unowned (null)
- [x] Example `chargen-app` package (`kind: application`, depends on `chargen`,
      configurable `minimum_role`) + index entry + README section; kept honest
      by `ExamplePackageTests`
- [x] Tests: `ApplicationPackageManifestTests` (parse/validation) +
      `PackageInstallServiceTests.ApplicationPackage_RegistersAndUnregisters_PortalApplication`
      (dep-gated plan, configure-resolved role, owning-package provenance,
      dependents block, uninstall reclaim)

## Deferred to v2 (post-merge polish)
- [ ] Dependency graph visualization (deps render as text/blockers today)
- [ ] Dbref linking in review panes (clickable, resolution tooltip)
- [ ] Inline diff rendering between versions (three-pane covers review)
- [ ] Authoring object picker: name search + zone/owner filters
- [ ] Export: push to git remote (credential management design needed)
- [ ] Flip SharpMUSH-Packages CI validate job from continue-on-error once the
      format v2 parser merges to main
- [ ] Typed configure `pattern` validation; `provides:`/`recommends:` semantics (20.20 reserved)
