# Area 20: Softcode Package Manager — TODO

## Pre-Implementation
- [ ] Review & confirm decisions (20.1–20.x) with project owner
- [ ] Identify any decisions that need revision based on current codebase state

## Implementation Tasks

### Phase 1: Package Format & Parsing
- [ ] Define package.yaml JSON schema (validate with tests)
- [ ] YAML parser for package manifests
- [ ] Validate intra-package ~refs resolve
- [ ] Validate dependency declarations parse correctly
- [ ] Handle `$well-known` and `?configure` ref types in manifest

### Phase 2: System Database Collections
- [ ] sys_packages collection (id, version, source_repo, source_path, installed_commit, installed_at, pinned_branch)
- [ ] sys_package_objects collection (package, ref, objid, type)
- [ ] sys_package_depends edge collection (_from, _to, constraint)
- [ ] sys_managed_attributes collection (package, objid, attr, baseline_hash, baseline_version)
- [ ] sys_remotes collection (name, url, trust, branch)
- [ ] Implement across all three backends (Arango, Surreal, in-memory)
- [ ] Create PM wizard player on first use (or via setup command)

### Phase 3: Plan Engine (Changeset Computation)
- [ ] Read live DB state for objects/attrs referenced by package
- [ ] Compare desired state (manifest) vs. live state
- [ ] Classify each change: create, modify, no-change
- [ ] For upgrades: three-way compare (baseline, live, new)
- [ ] Produce changeset data structure (objects, attrs, conflicts, actions)
- [ ] Dependency check: verify all deps met before planning

### Phase 4: Apply Engine
- [ ] Create objects (assign dbrefs, record in sys_package_objects)
- [ ] Set attributes (resolve ~refs to real dbrefs in values)
- [ ] Set flags, parents, locks on created objects
- [ ] Set owner = PM wizard on all created objects
- [ ] Update sys_managed_attributes with baseline hashes
- [ ] Update sys_packages with version + commit
- [ ] Handle `?configure` refs: prompt admin during review, substitute at apply

### Phase 5: Git Integration
- [ ] Clone remote to temp cache on browse
- [ ] Pull existing cache on subsequent browse
- [ ] Scan for package.yaml files (or read index.yaml)
- [ ] `git diff --name-only <installed_commit>..HEAD -- <path>` for update detection
- [ ] Branch pinning per remote
- [ ] Push support for authoring (auth: SSH key or token)

### Phase 6: Admin Panel — Consumer UI
- [ ] /admin/packages — status dashboard (installed, versions, badges)
- [ ] /admin/packages/remotes — manage remotes (add, remove, trust level)
- [ ] /admin/packages/browse — search across remotes
- [ ] /admin/packages/browse/{package} — detail, README, version list
- [ ] /admin/packages/review — changeset review screen
- [ ] Three-pane conflict view (base / live / new)
- [ ] Per-attr action selector (keep mine / take theirs / edit)
- [ ] Dangerous pattern scanner + visual flagging
- [ ] MUSHcode syntax highlighting (attr value display)
- [ ] Trust badges on source repos
- [ ] Dependency visualization (shows what else gets pulled in)
- [ ] Uninstall preview + confirm flow

### Phase 7: Admin Panel — Authoring UI
- [ ] /admin/packages/author — object picker (multi-select, search, zone filter)
- [ ] Relationship auto-discovery (selected objects that reference each other)
- [ ] Dbref scanner (find all #\d+ in attr values)
- [ ] Auto-classify ~internal refs (dbref is in selection)
- [ ] Batch resolution UI (classify unknowns as $well-known or ?configure)
- [ ] Per-object attribute selection (checkboxes, include/exclude)
- [ ] Metadata editor (name, version, description, deps, prefix, README)
- [ ] Manifest validation (all refs resolve, no unclassified dbrefs)
- [ ] Export: generate package.yaml
- [ ] Export: push to remote or download as file

### Phase 8: MUSHcode Rendering
- [ ] Tokenizer for MUSHcode (functions, substitutions, commands, dbrefs)
- [ ] Syntax highlighting renderer (HTML spans with CSS classes)
- [ ] Dangerous pattern detection (regex list: @force, @toad, etc.)
- [ ] Dbref linking (clickable, shows resolution tooltip)
- [ ] Diff rendering (inline, highlight changes between versions)

### Phase 9: Default Packages (SharpMUSH Official Repo)
- [ ] Create sharpmush-packages repo structure
- [ ] scene-system package (commands, HTTP handler hooks)
- [ ] bboard package (commands, HTTP handler hooks)
- [ ] events package (scheduled scene wrapper)
- [ ] who-where package (+who, +where)
- [ ] finger package (+finger)
- [ ] http-hooks package (base HTTP handler event objects)
- [ ] Each package: manifest, README, tested on clean install

## Testing
- [ ] Package manifest: valid YAML parses, invalid rejected with clear errors
- [ ] Plan engine: correct classification (create/modify/conflict/no-change)
- [ ] Three-way merge: all four cases in the truth table produce correct action
- [ ] Apply engine: objects created with correct owner, attrs set, refs resolved
- [ ] Git integration: clone, pull, update detection, branch pinning
- [ ] Dependency resolution: unmet deps block install with clear message
- [ ] Uninstall: objects destroyed, attrs removed, records cleaned
- [ ] Cross-package attrs: package A manages attr on package B's object correctly
- [ ] Dangerous pattern scanner: catches all listed patterns, no false negatives
- [ ] Authoring: dbref auto-classification correct for intra-package refs
- [ ] Full round-trip: author → export → install on fresh MUSH → verify state
