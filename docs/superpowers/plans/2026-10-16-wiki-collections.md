# Wiki collection navigation implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace sampled wiki navigation with authorized package collections and complete, paginated browsing as a foundation for #1970. This does not complete PF2e information architecture.
**Research:** [Give PF2e Readers Clear Navigable Context](../../research/pf2e-wiki-organization.md) informs the remaining authored-landing, metadata and identity milestones in the [updated design](../specs/2026-10-16-pf2e-wiki-organization.md#current-foundation-and-next-bounded-milestones).
**Architecture:** Existing durable package/page ownership supplies collection membership, checked by stable page IDs. Compose collection selection and search with existing WikiVisibility before storage counting/paging; retain canonical page keys and URLs. The server returns compact navigation and bounded localized page summaries; the portal uses existing D1 sidebar/layout components.
**Tech Stack:** .NET 11, Lightning, Blazor, TUnit/bUnit.
**Spec:** docs/superpowers/specs/2026-10-16-pf2e-wiki-organization.md

## Global Constraints
- Collection membership never grants read/edit permissions; visibility applies before pagination/counts.
- Never infer package ownership from page titles. Preserve page IDs, histories, translations, local edits, links and licensing.
- Existing identity and URLs remain authoritative; changing canonical keys/alias migration is a separately reviewed later step of the spec.
- No hard-coded PF2e names in generic navigation; display package identity intelligibly and preserve authored category hierarchy.
- Use existing theme tokens and components, responsive keyboard navigation and localized controls.

## Review Focus
- Pages beyond 500, including an entire 4,711-page package, must remain discoverable.
- Hidden/draft pages must not leak collection/category names or counts.
- Deleted/recreated page IDs must not inherit stale package ownership.
- A category shared by game/package pages must have separately correct scoped counts.
- Failed loading and rapid navigation must not display stale collection contents.

### Task 1: Storage selection/counting
- [x] Add real memory/Lightning regressions for page-ID inclusion/exclusion and title/category search before pagination, counts and restrictions.
- [x] Observe RED; implement selection in WikiVisibility and Lightning index admission; retain fast paths without predicates.
- [x] Run relevant wiki suites and formatting.

### Task 2: Authorized collection navigation API
- [x] Add controller tests for durable ownership, full counts beyond500, filtered categories, hidden-only collections and obsolete ownership.
- [x] Observe RED; add /api/wiki/navigation and /api/wiki/collections/{id}/pages using existing registry/services and visibility.
- [x] Return localized bodyless page summaries, exact total and bounded pages; no source/repository/secrets in collection DTOs.

### Task 3: Sidebar and collection browser — partial

Mechanical collection browsing and full-scale import proof are implemented. Authored task landing/navigation, explicit presentation metadata, structured facets and qualified canonical migration remain pending; a generic collection browser is not the finished PF2e layout.
- [x] Add bUnit RED for pinned-only sidebar, collection entry, no sampled500request, paginated/searchable collection/category directory and visible failures.
- [x] Implement service API calls, sidebar, collection browser routes and home collection entry points using existing theme.
- [x] Validate full-scale synthetic and real imported data, including the complete 4,711-page reference, scoped counts and deep pagination.
- [ ] Complete task-journey, keyboard, mobile/reflow and return-context acceptance with an authored collection landing; preserve existing canonical bookmarks while qualification remains deferred.
- [ ] Add readable localized collection metadata and an explicit owned landing, with authored groups preserving category introductions; validate destinations against actual imported content.
- [ ] Measure content-kind, facet and source/version coverage before selecting structured filters. Keep unknown coverage explicit.
- [ ] Implement separately reviewed qualified identity/alias/category-reference migration after its collision dry run and preservation contract.

### Task 4: Review, evidence and PR
- [x] Update design status and operator docs with research-backed next milestones, exact search scope, and remaining exploration/identity boundaries.
- [x] Run focused engine/database/controllers and complete bUnit once, then independent review and fix findings.
- [ ] Publish ready PR with issue linkage, attach it, wait current CI/review and merge only when confident.

## Reproduce the full-import acceptance evidence

Use the pinned SDK from `global.json`, a working rootless Podman/Docker endpoint for the test fixture, and disk-backed scratch. The source is the owned package repository at `2659a733a4519b76e64a727be2687794afc04f6b`, package `pf2e-remaster` version `1.1.1`. Download it explicitly before running the opt-in tests; neither test downloads source automatically and normal CI does not depend on a network fetch.

From the repository root:

```bash
export TMPDIR="$PWD/work/pf2e-wiki-qa/tmp"
export TMP="$TMPDIR"
export TEMP="$TMPDIR"
mkdir -p "$TMPDIR" "$PWD/work/pf2e-wiki-qa/packages" "$PWD/work/pf2e-wiki-qa/capture"
curl --fail --location \
  https://github.com/SharpMUSH/SharpMUSH.Packages/archive/2659a733a4519b76e64a727be2687794afc04f6b.tar.gz \
  --output "$PWD/work/pf2e-wiki-qa/packages.tar.gz"
tar -xzf "$PWD/work/pf2e-wiki-qa/packages.tar.gz" \
  --directory "$PWD/work/pf2e-wiki-qa/packages" --strip-components=1
export SHARPMUSH_PF2E_SOURCE="$PWD/work/pf2e-wiki-qa/packages/pf2e-remaster"
export SHARPMUSH_WIKI_CAPTURE_PATH="$PWD/work/pf2e-wiki-qa/capture"
# If using this host's rootless Podman:
export DOCKER_HOST="unix:///run/user/$(id -u)/podman/podman.sock"
export TESTCONTAINERS_RYUK_DISABLED=true
# Build these projects first if current artifacts are unavailable.
dotnet run --no-build --project SharpMUSH.Tests -- \
  --treenode-filter '/*/*/Pf2eCollectionImportTests/*' --maximum-parallel-tests 8
dotnet run --no-build --project SharpMUSH.Tests.BUnit -- \
  --treenode-filter '/*/*/WikiCollectionImportUiTests/*' --maximum-parallel-tests 8
```

Observed local evidence: the full actual import test passed `1/1` in approximately `2m45s`; the UI test passed `1/1` in `651ms`. Adjacent wiki tests passed `574/574` and the full bUnit suite passed `2,599/2,599`. Separate Chromium checks of the captured static component markup with real component CSS covered 390px/1100px geometry and native focus, with eight checks passing.

These are distinct evidence layers: the UI test exercises real component callbacks against actual API exports; the Chromium checks inspect captured markup/CSS separately. They are not a claim of an authenticated live Blazor application browser journey or an authored PF2e landing. Canonical migration, content-facet coverage, the full task-oriented layout and further accessibility acceptance remain pending.
