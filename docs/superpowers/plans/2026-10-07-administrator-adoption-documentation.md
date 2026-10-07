# Administrator Adoption Documentation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give a prospective MUSH administrator a tested migration, operations, and architecture path on sharpmush.com, backed by exact runtime guidance in SharpMUSH.

**Architecture:** SharpMUSH owns repository-coupled migration facts, deployment commands, Mermaid source, and TUnit contracts. SharpMUSH.Docs owns the concise Starlight presentation at sharpmush.com, including navigation, cross-links, current SDK claims, and verified screenshots. SharpMUSH.Guide remains the generated softcode reference and is unchanged.

**Tech Stack:** Markdown, MDX, Mermaid, Astro 5, Starlight, Node.js, TUnit/.NET 11

**Spec:** `docs/superpowers/specs/2026-10-07-administrator-adoption-documentation-design.md`

## Global Constraints

- Do not change the `:dev` image or automatic-update guidance.
- Do not rewrite starter wiki prompts or seed demo-only content.
- PennMUSH flatfile plus optional `mush.cnf` is the only supported direct import source.
- State explicitly that RhostMUSH and TinyMUX have no direct supported importer.
- Operational commands must already exist and name their assumed directory or Compose file.
- SharpMUSH.Docs summaries link to SharpMUSH for low-level procedure instead of copying long command blocks.
- Use only screenshots verified against shipped routes; never present a design board as live proof.

## Review Focus

- Migration rollback preserves the original PennMUSH world and distinguishes it from SharpMUSH's `.previous` world.
- Readers can distinguish LMDB world copies, Restic snapshots, NATS state, wiki assets, and plugin files.
- Every new sharpmush.com route is present in navigation and linked from the relevant existing journey page.
- Maintained install/plugin pages cannot silently return to a stale SDK major version.
- Every internal site link and image reference resolves during the offline content check and production build.

---

### Task 1: SharpMUSH documentation contracts

**Files:**
- Create: `SharpMUSH.Tests/Documentation/AdministratorGuideTests.cs`
- Modify: `SharpMUSH.Tests/SharpMUSH.Tests.csproj`

**Interfaces:**
- Consumes: Markdown copied into the test output as `Documentation/Repository/...`
- Produces: durable content and relative-link contracts for repository-owned operational truth

- [ ] **Step 1: Link the maintained documents into test output**

Add `Content` items for the two new guides, the architecture source, `docs/guides/writing-a-plugin.md`, `deploy/README.md`, and `deploy/connection-updates.md`.

- [ ] **Step 2: Write failing tests**

Create tests that require:

```csharp
[Test]
public async Task MigrationGuideNamesSupportedAndUnsupportedSources()
{
	var text = Read("docs/guides/pennmush-migration.md");
	await Assert.That(text).Contains("PennMUSH flatfile");
	await Assert.That(text).Contains("mush.cnf");
	await Assert.That(text).Contains("RhostMUSH");
	await Assert.That(text).Contains("TinyMUX");
	await Assert.That(text).Contains("no direct supported importer");
}
```

Separate tests require one-way import, rehearsal, validation, cutover, rollback, every migration acceptance domain, every operator topic, state/backup distinctions, and every architecture component. A shared Markdown-link test ignores fragment-only and absolute URLs, strips anchors, URL-decodes paths, and fails when a relative repository target is absent. Restrict the stale-version assertion to maintained adoption/plugin documents, not historical specs or benchmark records.

- [ ] **Step 3: Verify RED**

Run `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/AdministratorGuideTests/*"`.

Expected: FAIL because the new files do not exist.

- [ ] **Step 4: Commit the contract**

```bash
git add SharpMUSH.Tests/Documentation/AdministratorGuideTests.cs SharpMUSH.Tests/SharpMUSH.Tests.csproj
git commit -m "test: define administrator documentation contracts"
```

### Task 2: Repository PennMUSH migration runbook

**Files:**
- Create: `docs/guides/pennmush-migration.md`
- Reference: `tools/parity/README.md`
- Reference: `deploy/README.md`
- Reference: `SharpMUSH.Client/Pages/Setup.razor`

**Interfaces:**
- Consumes: actual portal import order, Lightning staging/promotion, storage preflight, parity evidence
- Produces: technical source linked by the public migration page

- [ ] **Step 1: Write eligibility and preparation**

Cover the supported PennMUSH flatfile plus optional `mush.cnf`, unsupported direct RhostMUSH/TinyMUX imports, one-way boundary, immutable source backup, inventory, external integrations, capacity, and disposable rehearsal target.

- [ ] **Step 2: Write rehearsal and diagnostics**

Document setup/import ordering, configuration before database import, handler/package choices, staged promotion, diagnostics, and the rule that a rehearsal never becomes authoritative implicitly.

- [ ] **Step 3: Write the validation matrix**

Cover identities/passwords, object counts/dbrefs, attributes/locks, flags/powers, channels, mail, configuration, handlers, packages, representative softcode, telnet/WebSocket login, portal login, and external integrations.

- [ ] **Step 4: Write cutover and rollback**

Define final source freeze, go/no-go evidence, endpoint change, observation window, `.previous`, rollback triggers, returning to PennMUSH, and restoring a SharpMUSH world copy.

- [ ] **Step 5: Run focused tests and commit**

Expected: migration contracts PASS while later contracts remain red.

```bash
git add docs/guides/pennmush-migration.md
git commit -m "docs: add safe PennMUSH migration runbook"
```

### Task 3: Repository operator handbook

**Files:**
- Create: `docs/guides/operator-handbook.md`
- Reference: `deploy/README.md`
- Reference: `deploy/connection-updates.md`

**Interfaces:**
- Consumes: exact maintained Compose and recovery procedures
- Produces: task map linked by the public operator page

- [ ] **Step 1: Write deployment and first-claim operations**

Cover Caddy versus Cloudflare, public/private ports, protected `/setup`, secrets, post-claim checks, readiness, health, logs, metrics, `@storage`, and connection survival boundaries.

- [ ] **Step 2: Write backup and restore rehearsal**

Distinguish LMDB copies, wiki assets, Restic, NATS JetStream, and plugins. Require an isolated restore, non-public boot, world/wiki validation, and a dated result record.

- [ ] **Step 3: Write upgrades, rollback, and incidents**

Leave image policy unchanged. Cover preflight, backup, storage headroom, controlled recreation, verification, deployment rollback versus data restore, storage/NATS/renderer/socket/authentication/security incidents, and secret-safe diagnostics.

- [ ] **Step 4: Run focused tests and commit**

Expected: migration and operator contracts PASS.

```bash
git add docs/guides/operator-handbook.md
git commit -m "docs: add SharpMUSH operator handbook"
```

### Task 4: Repository-owned deployment architecture

**Files:**
- Create: `docs/design/deployment-architecture.md`
- Reference: `deploy/connection-updates.md`

**Interfaces:**
- Consumes: current Compose topology
- Produces: first-party Mermaid source and failure-boundary reference

- [ ] **Step 1: Add the diagram**

Show browser REST/SignalR/WebSocket, MU* telnet, Caddy/Cloudflare ingress, `SharpMUSH.Server`, `SharpMUSH.SocketServer`, `SharpMUSH.RenderingWorker`, NATS, Lightning/LMDB, wiki assets, point-in-time copies, read-only Restic access, off-host storage, readiness, and Prometheus. Label protocols and ownership.

- [ ] **Step 2: Explain failure and persistence boundaries**

For each service, state owned state, restart effect, health signal, and backup coverage. Link connection-survival detail.

- [ ] **Step 3: Run focused tests and commit**

Expected: all `AdministratorGuideTests` PASS.

```bash
git add docs/design/deployment-architecture.md
git commit -m "docs: add first-party deployment architecture"
```

### Task 5: SharpMUSH.Docs content contracts

**Files:**
- Create: `SharpMUSH.Docs/scripts/check-content.js`
- Create: `SharpMUSH.Docs/scripts/check-content.test.js`
- Modify: `SharpMUSH.Docs/package.json`

**Interfaces:**
- Consumes: site pages, `astro.config.mjs`, and asset references
- Produces: `npm run check-content`, invoked before production build

- [ ] **Step 1: Write failing Node tests**

Use `node:test` with a temporary content tree. Test missing navigation routes, missing internal links, missing image assets, generic repeated alt text, `.NET 10` in maintained install/plugin pages, and a valid fixture. Name the production change each test catches.

- [ ] **Step 2: Verify RED**

Run `node --test scripts/check-content.test.js`.

Expected: FAIL because `check-content.js` does not exist.

- [ ] **Step 3: Implement the checker**

Export pure helpers plus a CLI. Resolve `/route` links against `src/content/docs/<route>.mdx`, relative images against the page path, and the new navigation slugs from `astro.config.mjs`. Check only maintained guide/reference/technical pages for stale SDK prose. Ignore absolute URLs and fragments.

- [ ] **Step 4: Wire scripts and verify GREEN**

Add `"test": "node --test scripts/*.test.js"`, `"check-content": "node scripts/check-content.js"`, and run `check-content` before `astro build` without duplicating the existing conversion step.

- [ ] **Step 5: Commit**

```bash
git add scripts package.json package-lock.json
git commit -m "test: validate documentation content contracts"
```

### Task 6: Public migration and operator journeys

**Files:**
- Create: `SharpMUSH.Docs/src/content/docs/guides/pennmush-migration.mdx`
- Create: `SharpMUSH.Docs/src/content/docs/guides/operator-handbook.mdx`
- Modify: `SharpMUSH.Docs/astro.config.mjs`
- Modify: `SharpMUSH.Docs/src/content/docs/guides/get-started.mdx`
- Modify: `SharpMUSH.Docs/src/content/docs/guides/docker-quickstart.mdx`
- Modify: `SharpMUSH.Docs/src/content/docs/guides/web-portal.mdx`
- Modify: `SharpMUSH.Docs/src/content/docs/reference/compatibility.mdx`
- Modify: `SharpMUSH.Docs/src/content/docs/reference/comparison.mdx`
- Modify: `SharpMUSH.Docs/src/content/docs/index.mdx`

**Interfaces:**
- Consumes: the two SharpMUSH technical guides
- Produces: decision-ready sharpmush.com routes and cross-links

- [ ] **Step 1: Add both pages and navigation**

Use Starlight `Steps`, `Aside`, and `LinkCard`. Migration presents eligibility, rehearsal, validation, cutover, and rollback with a prominent one-way warning and links to repository detail. Operations presents first claim, daily checks, backup/restore rehearsal, upgrade verification, incident entry points, and links to exact deploy sections.

- [ ] **Step 2: Repair the adopter journey**

Add links from Get Started, Docker, Web Portal import routes, Compatibility, Comparison, and the home PennMUSH card. Keep the demo secondary to installation and migration evidence.

- [ ] **Step 3: Run content checks and commit**

Expected: `npm test` and `npm run check-content` PASS.

```bash
git add astro.config.mjs src/content/docs
git commit -m "docs: add migration and operator journeys"
```

### Task 7: Current SDK and visual proof

**Files:**
- Modify: `SharpMUSH.Docs/src/content/docs/guides/local-install.mdx`
- Modify: `SharpMUSH.Docs/src/content/docs/guides/plugins.mdx`
- Modify: `SharpMUSH.Docs/src/content/docs/guides/web-portal.mdx`
- Add: verified images under `SharpMUSH.Docs/src/assets/guide/`

**Interfaces:**
- Consumes: `SharpMUSH/global.json` and verified current portal screens
- Produces: accurate SDK requirements, descriptive alt text, and product-state evidence

- [ ] **Step 1: Update SDK requirements**

Change the development prerequisite to the SDK version pinned in `global.json`, explain prerelease/roll-forward behavior briefly, change plugin examples from `net10.0` to `net11.0`, and correct the three repeated `Git Clone` alt texts to their actual tasks.

- [ ] **Step 2: Capture or reuse only shipped portal states**

Verify first-run setup, saved configuration, package review, and database import routes. Add only states that can be reached without inventing data or exposing secrets. Use task-specific filenames, captions, and alt text; omit unavailable privileged states.

- [ ] **Step 3: Add images to the portal guide**

Place each image beside the administrator job it proves. Keep prose useful without the image.

- [ ] **Step 4: Run content checks and commit**

```bash
git add src/assets/guide src/content/docs/guides
git commit -m "docs: update SDK guidance and portal visuals"
```

### Task 8: First-party site architecture

**Files:**
- Modify: `SharpMUSH.Docs/src/content/docs/technical/architecture.mdx`
- Add: `SharpMUSH.Docs/src/assets/guide/deployment-architecture.svg`

**Interfaces:**
- Consumes: SharpMUSH Mermaid topology and boundary prose
- Produces: reliable architecture route with no external single point of failure

- [ ] **Step 1: Replace the external-only page**

Embed a first-party rendered diagram and concise service/persistence/failure tables. Link the Mermaid source and detailed connection-update reference in SharpMUSH. DeepWiki may remain supplementary, not the only diagram.

- [ ] **Step 2: Run checks and production build**

Run `npm test`, `npm run check-content`, and `npm run build` with the SharpMUSH submodule at the matching commit. Expected: PASS with no broken MDX imports or routes.

- [ ] **Step 3: Commit**

```bash
git add src/assets/guide/deployment-architecture.svg src/content/docs/technical/architecture.mdx
git commit -m "docs: publish first-party deployment architecture"
```

### Task 9: Cross-repository verification

**Files:**
- Modify only when verification exposes an in-scope defect.

**Interfaces:**
- Consumes: completed SharpMUSH and SharpMUSH.Docs changes
- Produces: passing builds and an evidence-backed handoff

- [ ] **Step 1: Format SharpMUSH twice**

Run `dotnet format whitespace --folder SharpMUSH.Tests --exclude "**/bin/**" --exclude "**/obj/**"` twice. The second pass changes nothing.

- [ ] **Step 2: Run SharpMUSH verification**

Run the focused `AdministratorGuideTests`, then `dotnet build SharpMUSH.Tests/SharpMUSH.Tests.csproj`.

- [ ] **Step 3: Run SharpMUSH.Docs verification**

Run `npm test`, `npm run check-content`, and `npm run build` from a clean dependency install. Confirm all new routes appear in `dist/`.

- [ ] **Step 4: Inspect both diffs**

Run `git diff --check`, `git status --short`, and `git diff --stat` in both repositories. Exclude research reports, temporary clones, build output, and unrelated user work.

- [ ] **Step 5: Commit verification repairs separately**

Use a narrow `fix(docs): ...` commit only if verification finds an in-scope defect. Make no empty commit.

