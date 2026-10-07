# Administrator Adoption Documentation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish tested migration, operations, and deployment-architecture guidance from SharpMUSH into SharpMUSH.Guide without duplicating an editable source of truth.

**Architecture:** SharpMUSH owns the three repository-coupled Markdown sources and C# contract tests. SharpMUSH.Guide's existing guarded sync copies those selected documents into `Guides/` independently of the generated help-reference pipeline, with an offline shell fixture proving preservation and failure behavior.

**Tech Stack:** Markdown, Mermaid, TUnit/.NET 11, Bash, GitHub Actions

**Spec:** `docs/superpowers/specs/2026-10-07-administrator-adoption-documentation-design.md`

## Global Constraints

- Do not change the `:dev` image or automatic-update guidance.
- Do not rewrite starter wiki prompts or seed demo-only content.
- PennMUSH flatfile plus optional `mush.cnf` is the only supported direct import source.
- State explicitly that RhostMUSH and TinyMUX have no direct supported importer.
- Operational commands must already exist and name their assumed directory or Compose file.
- SharpMUSH is the editable source of truth; SharpMUSH.Guide receives generated copies.
- Use concise present-tense prose with no historical or changelog narration.

## Review Focus

- A missing upstream administrator guide must fail the Guide sync before any published guide changes.
- An unrelated hand-written Guide file must survive every sync.
- Relative links must resolve both in SharpMUSH and after publication under SharpMUSH.Guide's `Guides/` directory.
- A reader must not confuse a world copy, an off-host Restic snapshot, NATS state, or plugin files.
- Migration rollback must preserve the source PennMUSH world and identify SharpMUSH's `.previous` world separately.

---

### Task 1: Documentation contracts in SharpMUSH

**Files:**
- Create: `SharpMUSH.Tests/Documentation/AdministratorGuideTests.cs`
- Modify: `SharpMUSH.Tests/SharpMUSH.Tests.csproj`

**Interfaces:**
- Consumes: repository root derived from `AppContext.BaseDirectory`
- Produces: contract tests for the three source documents and maintained version claims

- [ ] **Step 1: Add the new Markdown files to the test output**

Add linked content entries for `docs/guides/pennmush-migration.md`, `docs/guides/operator-handbook.md`, `docs/design/deployment-architecture.md`, `docs/guides/writing-a-plugin.md`, `deploy/README.md`, and `deploy/connection-updates.md` under `Documentation/Repository/` in `SharpMUSH.Tests.csproj`.

- [ ] **Step 2: Write failing contract tests**

Create `AdministratorGuideTests.cs` with one test per document. Use a small `Read(string relative)` helper over copied test content. Assert concepts independently, for example:

```csharp
[Test]
public async Task MigrationGuideDefinesTheSupportedAndUnsupportedSources()
{
	var text = Read("docs/guides/pennmush-migration.md");
	await Assert.That(text).Contains("PennMUSH flatfile");
	await Assert.That(text).Contains("mush.cnf");
	await Assert.That(text).Contains("RhostMUSH");
	await Assert.That(text).Contains("TinyMUX");
	await Assert.That(text).Contains("no direct supported importer");
}
```

Additional tests require `one-way`, `rehearsal`, `validation`, `cutover`, `rollback`, and every acceptance domain from the spec; require the operator topics and the backup/state distinctions; require every architecture component; reject `.NET 10` only in the maintained guide set; and resolve every relative Markdown link in the three new files. Ignore fragment-only, `http:`, `https:`, and `mailto:` destinations. Strip anchors and URL-decode paths before checking them.

- [ ] **Step 3: Run the test and verify RED**

Run:

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/AdministratorGuideTests/*"
```

Expected: FAIL because the three source documents do not exist.

- [ ] **Step 4: Commit the failing contract**

```bash
git add SharpMUSH.Tests/Documentation/AdministratorGuideTests.cs SharpMUSH.Tests/SharpMUSH.Tests.csproj
git commit -m "test: define administrator guide contracts"
```

### Task 2: PennMUSH migration runbook

**Files:**
- Create: `docs/guides/pennmush-migration.md`
- Reference: `tools/parity/README.md`
- Reference: `deploy/README.md`
- Reference: `SharpMUSH.Client/Pages/Setup.razor`

**Interfaces:**
- Consumes: current portal import flow, Lightning staging/promotion behavior, parity evidence
- Produces: repository source published as `SharpMUSH.Guide/Guides/PennMUSH Migration.md`

- [ ] **Step 1: Write the support boundary and preparation sections**

State the supported source shape, unsupported direct sources, one-way boundary, source preservation, inventory, source dump/config capture, external-integration inventory, capacity preflight, and disposable target requirement.

- [ ] **Step 2: Write rehearsal and import steps**

Document `/setup` ordering and the later admin import route, `mush.cnf` before database import, handler/package choices, staged-world promotion, diagnostics, and the rule that rehearsal never becomes authoritative by accident.

- [ ] **Step 3: Write the acceptance matrix**

Use a checklist/table covering identities/passwords, objects/dbrefs, attributes/locks, flags/powers, channels, mail, configuration, handlers, packages, representative softcode, telnet/WebSocket login, portal login, and external integrations. Link developer parity evidence without making it an operator prerequisite.

- [ ] **Step 4: Write cutover and rollback**

Define go/no-go evidence, final source freeze, final import, DNS/client endpoint change, observation window, source-world retention, `.previous` semantics, rollback triggers, and the difference between switching back to PennMUSH and restoring a SharpMUSH world copy.

- [ ] **Step 5: Run the migration contracts**

Run the focused filter from Task 1. Expected: migration tests PASS; operator and architecture tests still FAIL.

- [ ] **Step 6: Commit**

```bash
git add docs/guides/pennmush-migration.md
git commit -m "docs: add safe PennMUSH migration runbook"
```

### Task 3: Operator handbook

**Files:**
- Create: `docs/guides/operator-handbook.md`
- Reference: `deploy/README.md`
- Reference: `deploy/connection-updates.md`

**Interfaces:**
- Consumes: supported Compose paths and existing detailed deployment procedures
- Produces: operational sequence published as `SharpMUSH.Guide/Guides/Operator Handbook.md`

- [ ] **Step 1: Write deployment selection and secure first claim**

Link the direct-Caddy and Cloudflare choices, enumerate public/private ports, require controlled access to `/setup`, identify secrets, and give post-claim checks.

- [ ] **Step 2: Write observation and routine-operation sections**

Cover `/ready`, `/api/health`, logs by service, Prometheus ports, `@storage`, `@backup/list`, process ownership, and connection survival boundaries. Use exact commands already present in `deploy/README.md` or link to that section rather than inventing variants.

- [ ] **Step 3: Write backup and restore rehearsal**

Distinguish LMDB point-in-time copies, wiki assets, Restic snapshots, NATS JetStream state, and plugin files. Require a scheduled rehearsal that restores into an isolated volume, boots it without public ingress, validates world and wiki data, and records date/result/operator.

- [ ] **Step 4: Write upgrades, rollback, and incidents**

Preserve current image policy. Add preflight, backup, storage headroom, controlled pull/recreate, readiness/functional checks, deployment rollback versus data restore, and incident trees for storage pressure, NATS failure, renderer failure, socket-server replacement, failed authentication, and suspected compromise. Diagnostic collection must redact `.env`, tokens, cookies, passwords, and world data.

- [ ] **Step 5: Run focused contracts**

Expected: migration and operator tests PASS; architecture tests still FAIL.

- [ ] **Step 6: Commit**

```bash
git add docs/guides/operator-handbook.md
git commit -m "docs: add SharpMUSH operator handbook"
```

### Task 4: First-party deployment architecture

**Files:**
- Create: `docs/design/deployment-architecture.md`
- Reference: `docs/design/d1/boards/29-config-section-saved.png`
- Reference: `deploy/connection-updates.md`

**Interfaces:**
- Consumes: deployed service topology and shipped configuration UI proof
- Produces: Mermaid source published as `SharpMUSH.Guide/Guides/Deployment Architecture.md`

- [ ] **Step 1: Add the Mermaid topology**

Show browser REST/SignalR/WebSocket, MU* telnet, ingress choice, `SharpMUSH.Server`, `SharpMUSH.SocketServer`, `SharpMUSH.RenderingWorker`, NATS, Lightning/LMDB, wiki assets, point-in-time copies, read-only Restic access, off-host storage, readiness, and Prometheus. Label protocols and ownership on edges.

- [ ] **Step 2: Explain failure and persistence boundaries**

Give one concise subsection per service: owned state, restart effect, health signal, and what a backup does or does not include. Link `deploy/connection-updates.md` for socket continuity.

- [ ] **Step 3: Add one accurate portal visual**

Embed `docs/design/d1/boards/29-config-section-saved.png` only after checking it against the shipped route. Caption it as `/admin/config` and state that it illustrates the saved configuration state rather than the whole admin surface. If it is stale, omit it and record why in the commit message instead of presenting a mockup as proof.

- [ ] **Step 4: Run focused contracts and link checks**

Expected: all `AdministratorGuideTests` PASS.

- [ ] **Step 5: Commit**

```bash
git add docs/design/deployment-architecture.md
git commit -m "docs: add first-party deployment architecture"
```

### Task 5: Guarded administrator-guide sync in SharpMUSH.Guide

**Files:**
- Create: `/home/grave/RiderProjects/SharpMUSH.Guide/.github/scripts/update_administrator_guides.sh`
- Create: `/home/grave/RiderProjects/SharpMUSH.Guide/.github/scripts/test_administrator_guides.sh`
- Modify: `/home/grave/RiderProjects/SharpMUSH.Guide/.github/workflows/sync-documentation.yml`
- Modify: `/home/grave/RiderProjects/SharpMUSH.Guide/README.md`
- Generated: `/home/grave/RiderProjects/SharpMUSH.Guide/Guides/PennMUSH Migration.md`
- Generated: `/home/grave/RiderProjects/SharpMUSH.Guide/Guides/Operator Handbook.md`
- Generated: `/home/grave/RiderProjects/SharpMUSH.Guide/Guides/Deployment Architecture.md`

**Interfaces:**
- Consumes: SharpMUSH checkout containing the three exact source paths
- Produces: stable Guide filenames and a non-destructive guarded sync

- [ ] **Step 1: Write the failing offline shell test**

Build a temporary fake SharpMUSH tree and fake Guide output. Assert that:

```bash
update_administrator_guides.sh "$source" "$target"
test -f "$target/PennMUSH Migration.md"
test -f "$target/Operator Handbook.md"
test -f "$target/Deployment Architecture.md"
test -f "$target/Code Style.md"
```

Then remove one source, require a nonzero exit, and compare checksums to prove the target was unchanged.

- [ ] **Step 2: Run the shell test and verify RED**

Run `.github/scripts/test_administrator_guides.sh` from SharpMUSH.Guide. Expected: FAIL because the updater does not exist.

- [ ] **Step 3: Implement the atomic copy script**

The script takes `<sharpmush-root> <guide-directory>`, validates all three sources before mutation, copies them through a temporary directory, then installs the three stable filenames. It touches no other file in `Guides/`.

- [ ] **Step 4: Run the shell test and verify GREEN**

Run `.github/scripts/test_administrator_guides.sh`. Expected: PASS with no network.

- [ ] **Step 5: Extend the scheduled workflow**

Expand sparse checkout to include `docs/guides/pennmush-migration.md`, `docs/guides/operator-handbook.md`, and `docs/design/deployment-architecture.md`. Call the updater after help processing and include the administrator-guide result in the workflow summary. Preserve the main-branch push guard.

- [ ] **Step 6: Update repository guidance and generate the current copies**

Explain in `README.md` that command/function/configuration pages and the three administrator guides are generated from SharpMUSH, while other `Guides/` files remain hand-written here. Run the updater against the current SharpMUSH worktree.

- [ ] **Step 7: Run all Guide tests**

Run:

```bash
.github/scripts/test_administrator_guides.sh
.github/scripts/test_workflow.sh
```

Expected: PASS; `git diff --check` produces no output.

- [ ] **Step 8: Commit in SharpMUSH.Guide**

```bash
git add .github README.md Guides
git commit -m "docs: publish administrator adoption guides"
```

### Task 6: Cross-repository verification

**Files:**
- Modify only if verification exposes a defect in files already in scope.

**Interfaces:**
- Consumes: completed changes in both repositories
- Produces: evidence that the sources, generated copies, links, format, and tests agree

- [ ] **Step 1: Compare published copies byte-for-byte**

Use `cmp` for each SharpMUSH source and SharpMUSH.Guide destination. Expected: all return 0.

- [ ] **Step 2: Run SharpMUSH formatting twice**

```bash
dotnet format whitespace --folder SharpMUSH.Tests --exclude "**/bin/**" --exclude "**/obj/**"
dotnet format whitespace --folder SharpMUSH.Tests --exclude "**/bin/**" --exclude "**/obj/**"
```

Expected: second pass changes nothing.

- [ ] **Step 3: Run focused and dependent SharpMUSH verification**

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/AdministratorGuideTests/*"
dotnet build SharpMUSH.Tests/SharpMUSH.Tests.csproj
```

Expected: PASS.

- [ ] **Step 4: Run Guide verification again from a clean fixture**

```bash
.github/scripts/test_administrator_guides.sh
.github/scripts/test_workflow.sh
```

Expected: PASS.

- [ ] **Step 5: Inspect both diffs**

Run `git diff --check`, `git status --short`, and `git diff --stat` in both repositories. Confirm no research artifacts, temporary fixture directories, or unrelated user changes are staged.

- [ ] **Step 6: Commit any verification-only repairs separately**

Use a narrow `fix(docs): ...` commit only when verification required an in-scope correction. Otherwise make no empty commit.

