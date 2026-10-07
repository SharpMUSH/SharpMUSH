# Administrator adoption documentation

## Purpose

Give an experienced MUSH administrator enough current, repository-owned evidence to rehearse a PennMUSH import, operate and recover a SharpMUSH deployment, and understand the service boundaries without relying on marketing claims or an external diagram service.

The work spans the three documentation surfaces according to their existing responsibilities. `SharpMUSH` owns facts coupled to the runtime, deployment files, and tests. `SharpMUSH.Docs` owns the adopter-facing presentation published at `sharpmush.com`. `SharpMUSH.Guide` remains the generated command, function, configuration, and softcode reference; this slice changes it only if a reference link is genuinely needed.

## Scope

This slice delivers:

- a safe PennMUSH migration runbook;
- an operator handbook that connects the existing deployment facts into one task-oriented path;
- a first-party architecture diagram stored as text in the repository;
- links to existing portal screenshots only where they represent the shipped interface;
- automated documentation checks for internal links and framework-version claims;
- native Starlight pages, navigation, screenshots, and build verification in SharpMUSH.Docs.

It does not:

- change the `:dev` image or automatic-update guidance;
- rewrite the starter wiki or its administrator prompts;
- seed demo-only characters, rooms, or scenes;
- claim a supported RhostMUSH or TinyMUX importer;
- duplicate the generated command and function reference.

## Audience and success criteria

The primary reader already knows how to run a MUSH but may not know .NET, LMDB, NATS, or SharpMUSH's portal. After reading the new material, that reader can:

1. decide whether their source world is eligible for the supported import path;
2. preserve the source world and rehearse an import without making the rehearsal authoritative;
3. validate imported data and softcode before cutover;
4. identify the rollback artifact and reverse a failed cutover;
5. install, secure, observe, back up, restore, upgrade, and troubleshoot the supported Compose deployment;
6. explain which process owns HTTP, telnet/WebSocket connections, rendering, messaging, world storage, and backups.

Every operational command must already exist in the repository and must name the Compose file or working directory it assumes. No page may imply that a backup is proven until a restore rehearsal has succeeded.

## Information architecture

### Migration runbook

Create `docs/guides/pennmush-migration.md` as the task-oriented entry point.

It begins with a support boundary:

- PennMUSH flatfile plus optional `mush.cnf` is the supported source shape.
- RhostMUSH and TinyMUX have no direct supported importer. Administrators must first convert through a compatible PennMUSH export or treat the migration as an unsupported engineering project.
- Import is one-way. The source world remains the rollback authority until the SharpMUSH cutover is accepted.

The runbook then covers inventory, source backup, disposable target deployment, capacity preflight, portal import sequence, diagnostic review, acceptance testing, cutover, and rollback. The acceptance checklist covers identities and passwords, object counts and dbrefs, attributes and locks, flags and powers, channels, mail, configuration, handler objects, package selection, representative softcode, client login, portal login, and external integrations. Unsupported or partially supported domains are stated rather than silently omitted.

The runbook links to the parity harness for behavioral evidence but does not require an administrator to run the developer harness for a normal import.

### Operator handbook

Create `docs/guides/operator-handbook.md`. It is a map and checklist, not a second copy of `deploy/README.md`. It links to the exact detailed sections in `deploy/README.md` while supplying the operating sequence:

1. select the direct-Caddy or Cloudflare deployment;
2. protect the first `/setup` claim and configure secrets;
3. verify readiness, health, logs, metrics, storage, and public ports;
4. enable scheduled world copies and optional off-host Restic snapshots;
5. run and record a restore rehearsal;
6. perform an upgrade with preflight, backup, health verification, and rollback criteria;
7. respond to storage, messaging, process, and authentication incidents;
8. collect a bounded diagnostic bundle without publishing secrets.

The handbook distinguishes:

- an LMDB world copy from an off-host Restic snapshot;
- the `app-data` volume from NATS JetStream state and plugin files;
- readiness from process liveness;
- renderer restarts, engine restarts, and socket-server replacement;
- a deployment rollback from a world-data restore.

It preserves the current development-image policy unchanged.

### Architecture reference

Create `docs/design/deployment-architecture.md` with a Mermaid diagram kept next to explanatory prose. The diagram shows:

- browser REST, SignalR, and WebSocket traffic;
- MU* client telnet traffic;
- Caddy or Cloudflare ingress;
- `SharpMUSH.Server`;
- `SharpMUSH.SocketServer`;
- `SharpMUSH.RenderingWorker`;
- NATS messaging;
- Lightning/LMDB world storage and wiki assets;
- point-in-time world copies;
- the read-only Restic sidecar path to off-host storage;
- health and Prometheus observation points.

Arrows name protocols or data ownership. The prose states the failure boundary for each service and links to `deploy/connection-updates.md` for connection survival behavior.

Existing screenshots under `docs/design/d1/boards/` may be linked from the operator or architecture documents only when the corresponding UI is shipped and the caption identifies the represented route. Design-only or stale boards remain design references and are not presented as current product proof.

### SharpMUSH.Docs publication

`SharpMUSH.Docs` receives native MDX pages under `src/content/docs/guides/` for migration and operations, plus an expanded `technical/architecture.mdx`. These pages use Starlight's `Steps`, `Aside`, `LinkCard`, and image support to present the same contracts in an adopter-facing form. They link to exact SharpMUSH repository sections for commands and low-level recovery detail rather than copying long command blocks that would drift.

The site navigation adds **Migrate from PennMUSH** and **Operate SharpMUSH** directly after **Run with Docker**. Get Started, the home-page PennMUSH card, Compatibility, Comparison, Web Portal, and Docker pages link into those decision paths. The development and plugin pages derive their visible SDK requirement from the repository's current `global.json` value in prose maintained for the same release; this slice updates the present stale .NET 10 claims to .NET 11 and adds a build-time test that rejects regressions in maintained pages.

Screenshots live in `src/assets/guide/` with task-specific names and alt text. Only shipped screens are used. The portal guide receives annotated or tightly cropped views of first-run setup, configuration, packages, and import when those current states can be captured or verified; unavailable privileged states use no mock image. The architecture page embeds a first-party diagram stored with the site and retains Mermaid source in SharpMUSH.

## Verification

Documentation verification belongs in `SharpMUSH.Tests` so it runs with the existing suite.

Add focused tests that:

- ensure the new Markdown files exist in the repository;
- resolve relative Markdown links and fail on missing repository targets;
- reject literal `.NET 10` claims in maintained administrator and plugin guidance;
- require the migration guide to state PennMUSH support, RhostMUSH/TinyMUX status, one-way import, rehearsal, validation, cutover, and rollback;
- require the operator handbook to cover first claim, readiness, backup, restore rehearsal, upgrade, rollback, logs, metrics, storage, and incident handling;
- require the architecture document to name every deployed service, NATS, Lightning/LMDB, wiki assets, backups, and observation endpoints.

Tests assert durable contractual concepts, not exact paragraphs or formatting. Existing documentation tests and `dotnet format whitespace` remain green.

`SharpMUSH.Docs` adds an offline content-contract script that checks the new routes, navigation entries, current SDK claims, task-specific image alt text, and internal links before `astro build`. The normal production build remains the final proof that MDX and Starlight components compile. SharpMUSH.Guide's generated-reference sync remains unchanged.

## Writing rules

- Lead with the action or decision, then explain why.
- Use concise present-tense prose and exact commands.
- Do not use historical or changelog-style narration.
- Link instead of duplicating detailed material already owned by `deploy/README.md`.
- Label destructive and irreversible steps immediately before the command.
- Never imply support that the importer or tests do not demonstrate.
- Keep all architecture source in the repository; external rendered copies are optional mirrors.

## Delivery order

1. Add failing documentation-contract tests.
2. Add the migration runbook until its contract passes.
3. Add the operator handbook until its contract passes.
4. Add the architecture reference and diagram until its contract passes.
5. Repair stale `.NET 10` guidance covered by the version test.
6. Add the SharpMUSH.Docs migration and operator pages, navigation, cross-links, current SDK requirements, and verified screenshots.
7. Replace the external-only architecture page with the first-party architecture reference.
8. Run focused SharpMUSH tests, formatting verification, SharpMUSH.Docs content contracts and production build, and link validation.
