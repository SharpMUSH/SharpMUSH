# Dependency-aware Package Changes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Review and execute dependency-aware install/uninstall/plugin-state batches without hidden cascades.

**Architecture:** A server batch coordinator projects existing dependency metadata and wraps the existing installer, plugin administration and package operation runner. The portal keeps an editable intent queue; the server owns ordering, trust and stale-state validation. No runtime activation flag or alternate install engine.

**Tech Stack:** .NET 11 RC2, existing Mediator/store services, ASP.NET Core, Blazor/MudBlazor, TUnit/bUnit.

**Spec:** `docs/superpowers/specs/2026-10-17-package-activation-batch-design.md` (user chose existing install/uninstall; root preflight approved).

## Global constraints

- Functional choice and root preflight approved. Implement the existing install/uninstall workflow only. No production deployment.
- Label uninstall honestly. No new user-data deletion, force removal, implicit dependent cascade, automatic conflict decision, or batch rollback claim.
- Preserve `PackagesAdmin`, separate `ServerOperate`, per-apply `AllowPluginCode`/maximum-version checks, existing backups and lifecycle queue ordering.
- Temporary work uses `/temp/codex/package-activation-batch`; preserve existing untracked `work/` and the merged import branch.

## Review focus

Mixed contradictory requests; source/version/state drift after review; dependency cycles and unavailable prerequisites; partial completion across a plugin restart boundary; cancellation or lost progress with unknown outcome. Each is covered below.

## Task 1: One dependency projection and truthful batch contract

Files: new `SharpMUSH.Contracts/API/PackageBatchModels.cs`; new `SharpMUSH.Server/Services/PackageBatchPlanner.cs`; extend/extract `SharpMUSH.Server/Services/BundledPackagePlan.cs`; tests `SharpMUSH.Tests/Server/PackageBatchPlannerTests.cs` and existing `BundledPackagePlanTests.cs`.

Interface: typed install/uninstall/plugin-state intent; plan response contains explicit/proposed steps, reasons, blockers, source/state fingerprint and restart boundaries. Keep actual installed revision and desired installation state distinct from plugin enabled/running state.

- [ ] Write failing cases: A requires B; conflicting requested remove-B/install-A; mixed remove-A/remove-B ordered A then B; missing/incompatible source/version; diamond closure only once; cycle with full diagnostic; independent removal unaffected by unrelated invalid component.
- [ ] Run only the planner classes and verify the intended failures.
- [ ] Build projection from existing registry edges/manifests/discovery and reuse version predicates. Keep the bundled selector an adapter; do not add a dependency store.
- [ ] Verify planner and existing bundled ordering tests, then commit the focused unit.

## Task 2: Reviewed execution, safe plugin state changes and progress

Files: new `SharpMUSH.Server/Services/PackageBatchService.cs` and `PackageBatchRunRegistry.cs`; new `SharpMUSH.Server/Controllers/PackageBatchesController.cs`; modify `PluginAdministrationService.cs`, `PackagePluginsController.cs`, `PackagesController.cs`, relevant registration; extract its shared catalogue/upload/remote fetching into `SharpMUSH.Server/Services/PackageManifestResolver.cs`; tests `SharpMUSH.Tests/Server/PackageBatchExecutionTests.cs` and `Server/Controllers/PackageBatchesControllerTests.cs`.

Interface: `PlanAsync(request, ct)`, `ApplyAsync(reviewedRequest, actor, ct)`, `GetRun(id, actor)`. Tokens bind source and state, not client-trusted ordering. Results distinguish completed/failed/unrun/awaiting-restart/unknown-after-expiry.

- [ ] Write failing cases for stale revision/commit/trust/plugin state, lost permission, missing code consent, one pre-batch backup, backup refusal causing zero writes, and exactly approved removals.
- [ ] Add actual sequencing tests: prerequisite installs first; removals reverse dependencies; later preflight conflict leaves completed prerequisite intact; retry replans; plugin-on pauses dependent installation until running after restart; plugin-off never silently uninstalls packages or restores declined choices.
- [ ] Test cancellation before start versus after committed steps, lost/expired status requiring refresh, and permission-scoped progress. No automatic rollback assertions.
- [ ] Run these tests RED, implement the coordinator through the existing queue/gate/installer, and separate plugin state mutation from hidden cascades/nested runner calls.
- [ ] Verify GREEN, existing plugin-dependent and package-operation tests, and controller authorization tests; commit.

## Task 3: Editable portal queue and reviewed results

Files: `SharpMUSH.Client/Services/PackagesAdminService.cs`; `Pages/Admin/Packages/{AdminPackages,AdminPackageBrowse,PluginRow,InstalledPackageRow,AdminPackageReview}.razor`; new `PackageBatchReview.razor`; relevant CSS and all resource locales; tests `SharpMUSH.Tests.BUnit/Pages/PackageBatchReviewTests.cs` plus existing package page tests.

- [ ] Write RED journeys for selecting/removing/changing requests, explicitly including prerequisites, mixed actions, blocked requests, direct blocker-free plugin-off, and existing uninstall confirmation without a redundant blocker modal.
- [ ] Test per-step progress, partial failure/review links, restart-pending truth, fresh retry, and no success claim on lost status. Keep configure secrets out of durable browser storage.
- [ ] Implement controls over the server plan, not client ordering. Preserve current individual apply review/conflict/code-trust UI and refresh navigation from actual server state.
- [ ] Verify keyboard operation, labelled controls, live status announcements and 390px layout; test all localization keys; commit.

## Task 4: Acceptance and review

- [ ] Exercise a real installed dependency pair through preview and batch apply, then the reviewed reverse uninstall; verify existing package data/registry effects match ordinary operations.
- [ ] Exercise a plugin restart boundary without claiming hot-load/unload, and a partial failure followed by fresh review/retry.
- [ ] Update the package guide with truthful installation/removal effects and unsupported retained-content suspension; run affected backend/bUnit suites and required CI once.
- [ ] Obtain independent source review, address comments and create a ready PR referencing #2008 only after the approved functional scope is fulfilled. Do not close the retained-content interpretation through this workflow by assertion.

Implementation is approved and runs with normal TDD, formatting gates and affected suites. Task 4 requires real isolated database integration evidence. Execution remains the root's existing parallel-worker workflow; no production actions are authorized here.
