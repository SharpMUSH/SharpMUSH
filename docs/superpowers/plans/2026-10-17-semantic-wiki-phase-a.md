# Semantic wiki Phase A Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan. Steps use checkbox syntax for tracking.

**Goal:** Deliver a reproducible PF2e semantic dry run, an independent authorized reference evaluator, and an exhaustive current-write-path matrix before implementing indexes.

**Architecture:** An offline console tool owns experimental contracts and extraction. It references the existing wiki models and permission decisions, never registers a production service or endpoint. Its deliberately slow canonical scan is the correctness oracle for Phase B/C. Separate reviewed plans govern those phases and the recovery/load gate; completing this milestone is not completing the whole feature.

**Tech Stack:** .NET 11 pinned SDK, C# 15 typed unions, existing YamlDotNet/Markdig dependencies, TUnit, existing WikiVisibility/WikiAccessService, pinned local PF2e pack.

**Spec:** [Approved architecture](../specs/2026-10-16-semantic-wiki-architecture-design.md), especially §§3–5 and Phase A in §11. Related issue #1970 and design PR #2016.

## Global Constraints

- No production DI/API, migration, persistent semantic indexes, result cache, or deployment. All experimental types live in `tools/WikiSemantics` until measured and reviewed.
- Source pack is immutable commit `2659a733a4519b76e64a727be2687794afc04f6b`, package `pf2e-remaster` 1.1.1. It contains 4,711 pages, including 4,580 Main, 130 Category, 1 Help. Verify input content rather than trusting a supplied commit string. Do not duplicate the corpus in git.
- Portable identity is package + namespace + explicit source key, never title. Opaque local IDs and explicit alias migrations prevent deleted-target retargeting. Missing data differs from zero/false.
- Registered value types are PageRef, signed integer, Boolean, and bounded normalized keyword. No general strings, implicit inference or prerequisite evaluation. Class and tradition are multivalued; level/rank single-valued. Reject invalid schemas/types/cardinality/quota before returning canonical facts.
- A reference query names one collection, at most four conjuncts and six scalar columns, page size 25 default / 100 maximum. Allow equality and integer ranges; order by immutable ID or a single-valued integer then ID (missing values last). Refuse unsupported combinations. Counts/facets are explicitly requested and deduplicate subjects.
- Apply existing server-resolved WikiVisibility to source and relation target before membership, projection, counts, facets and lookahead. Reject a nonempty Search filter rather than silently adding unsupported grammar. Missing/unreadable supplied PageRefs have the same outcome. No claim of runtime revocation protection: Phase A snapshots are immutable test inputs, not trusted production authorization stamps.
- Oracle may scan all canonical facts offline; it must not reuse future index planning. It provides no performance, budget, cursor-security or transaction guarantees.
- Temp/build output uses `/temp/codex/semantic-wiki` or workspace `work/semantic-wiki-prototype` with TMPDIR/TMP/TEMP. Use pinned SDK, normal formatting gates and a shared build lock. Do not install dependencies. Do not alter upstream Markdown, license notices, or unrelated branches/files.
- User authorized parallel work. Contracts precede evaluator/extractor; thereafter disjoint owners can implement in parallel. Root alone edits shared project/solution files and commits integrated changes. Serialize builds using the shared lock. Each implementation task receives a separate review.

## Review Focus

1. Hidden PageRef values must not change visible membership, projections, exact counts or facets, including a public subject pointing to a private target.
2. Same-title and deleted/recreated pages must not merge; explicit rename migration must preserve portable identity without guessing.
3. Extraction must use anchored authored fields, distinguish rank from level, preserve multivalue class facts and report unknown versatile ancestry instead of inventing links.
4. A partial or malformed corpus must not masquerade as the pinned fixture; provenance and deterministic output must be reproducible.
5. Current page body revision is insufficient for metadata/ACL/ownership. The matrix must expose this, distinguish import compensation from rollback, and detect unclassified store methods.

## Task 1: Experimental contracts and canonical validation

**Files:** Create `tools/WikiSemantics/WikiSemantics.csproj`, `Program.cs`, `Models.cs`, `CanonicalFacts.cs`; `SharpMUSH.Tests/Wiki/Semantics/SemanticContractTests.cs`. Root adds tool ProjectReference to `SharpMUSH.Tests/SharpMUSH.Tests.csproj` and solution entry.

- [x] Add buildable tool shell (net11, executable, nonpackable, warnings errors, ASP.NET framework ref, Library ProjectReference), with entry point explaining prototype status until CLI task.
- [x] Define immutable portable keys, local PageRefs, keywords, C#15 value union, property definitions, provenance, facts, page snapshots with collection membership, query predicates/projection/sort/results, and explicit result/failure unions. No nullable service results or discriminator plus nullable payload. Use existing conventions.
- [x] Include experimental stamp records naming semantic commit/index generation/schema/requirements/collection/partition/page/translation versions; document these as planned contracts without manufactured live guarantees. Document source-owned schema and local override provenance. Visibility class descriptor carries namespace/category/rule IDs, publication and draft author; scope grants remain external.
- [x] Write failing validation tests before implementation: bounded UTF-8 keyword normalization, case/canonical duplicates, enum rejection, wrong type, single/multiple cardinality, unknown vs zero/false, signed integer extremes, ambiguous portable aliases, target identity after delete/recreate. Schema version and definition immutability are explicit.
- [x] Implement minimal pure validation returning canonical sorted/deduplicated facts or typed error. Enforce spec fact limits (128/subject, 32/property, 64KiB canonical data, 256 UTF8 bytes/keyword). No exceptions for ordinary invalid input. Cancellation checked in loops.
- [x] Run focused tests RED then GREEN; format twice. Publish exact interfaces for Tasks 2/3. Review spec and quality before marking complete.

## Task 2: Independent authorized reference evaluator

**Files:** `tools/WikiSemantics/ReferenceEvaluator.cs`; `SharpMUSH.Tests/Wiki/Semantics/SemanticReferenceTests.cs`, `SemanticPermissionTests.cs`.

- [x] Write RED tests on small explicit graphs for equality/range/conjunction, integer total order, missing-last ordering, duplicates, counts and enum facets. Pin limit±1 shape tests and multivalue-order rejection.
- [x] Implement full canonical-fact scan with cancellation; collection scope and existing WikiVisibility admit both relation endpoints before predicate/projection/aggregation. Deduplicate subjects globally before paging. Return explicit invalid query/target outcome instead of guessing. Optional aggregates are complete offline results; do not present bounded runtime behavior.
- [x] Use real WikiAccessService with existing in-memory wiki test helper to build visibility for no-wiki.read admin, admin without wiki.drafts, own drafts plus category/namespace/page restrictions, null/empty/excluded collection filters. Do not duplicate grant logic in the oracle.
- [x] Pin Review Focus 1: hidden targets cannot satisfy predicates or appear in projected values/counts/facets; hidden-only graph changes leave public results unchanged; missing and unreadable target predicates return the same outcome.
- [x] Pin Review Focus 2: same title/different namespace, deleted-target replacement, explicit source rename mapping. Include cancellation and invalid unsupported Search.
- [x] Run RED/GREEN focused tests and existing WikiAccessServiceTests; report commands/results; independent task review.

## Task 3: Verified pinned PF2e extraction and dry-run CLI

**Files:** `tools/WikiSemantics/PinnedDataset.cs`, `Pf2eExtractor.cs`, `DryRunReport.cs`, `Program.cs`, `README.md`; fixtures under `SharpMUSH.Tests/Wiki/Semantics/Fixtures`; `SemanticExtractionTests.cs`, `SemanticDatasetTests.cs`.

- [x] RED tests verify malformed/missing files, path traversal, source-key collisions, deterministic output, unsupported source formats and corpus tampering. Create small authored-format fixtures, retain license attribution. No network in tests.
- [x] Parse existing PackageManifestService/YamlDotNet types. Bind full-source validation to a checked content lock from the Git tree at the pinned commit; lock includes only declared/required files and enough exact hashes to refuse substitutions, never an arbitrary claimed commit. Full source remains external. Check manifest and Markdown referenced paths, reject traversal/symlinks outside root. Hash before extraction with bounded reads and cancellation. State precisely what lock proves.
- [x] Assign deterministic opaque fixture IDs independently of titles/slugs; retain explicit baseline portable namespace/slug keys. Emit provenance (package/version/commit/source blob/extraction version/evidence field/local override false). Copy license references to reports.
- [x] Anchored extraction only: leading authored item/feat/feature level; spell/focus/cantrip/ritual rank; explicit Traditions; Class Feat Traits against reviewed class IDs; heritage Ancestry against reviewed identities; source-book footer. Never inspect arbitrary prose for gameplay meaning. Preserve full source citations apart from scalar source-book keyword. Missing/unsupported is reported as unknown/reject, not a fabricated fact.
- [x] Pin Review Focus 3 with poison_resistance multi-class, electric_arc rank not level, ancient_blooded_dwarf ancestry, versatile heritage unknown, hub exclusions. Pin Review Focus 4 via independently established full-pack expectations: 3,396 level subjects, 659 rank; 408 tradition subjects/894 values; 904 class subjects/977 values (47 Alchemist subjects); 99 ancestry refs/7 unknown versatile heritages; 4,580 source-book facts. Investigate differences; do not loosen expected values to match a bug.
- [x] CLI accepts `dry-run --source <directory> --output <new-directory>`, writes deterministic UTF8 JSONL canonical facts plus provenance/coverage/reject/unknown report without upstream mutation. Refuse existing output directory, stage then rename, clean failure scratch, nonzero exit on verification failure. Provide README example using disk temp env. No network fetch or live wiki mutation.
- [x] Run default sample tests, then actual verified full corpus dry run twice and compare bytes. Run representative oracle queries (Alchemist feats by level; Dwarf heritages) against authored examples, record actual fact counts and licenses. Full acceptance explicitly gated by local source path, not silently skipped as proof.
- [x] Independent task review, including report and output samples.

## Task 4: Mutation coverage and next-phase gate

**Files:** `docs/design/wiki-semantic-mutation-coverage.md`; `SharpMUSH.Tests/Wiki/Semantics/SemanticMutationCoverageTests.cs`; `docs/reports/2026-10-17-semantic-wiki-phase-a.md`.

- [x] Map every IWikiStore API explicitly as read, presentation-only or semantic mutation. Reflect methods to fail when an API becomes unclassified; this is drift detection, not proof of transactional correctness. Include package wiki ownership mutations outside IWikiStore.
- [x] Pin Review Focus 5 using real Lightning fixture probes for title/metadata not advancing body revision, body edits advancing it, translation independent revisions/CAS, requirement writes, delete/recreate identity, ownership membership changes independent of body. Reuse existing tests where sufficient rather than copying suites.
- [x] Document callers through portal/in-game/startup/package create/update/adoption/removal/undo/uninstall, revision body restore, and requirements. Include transaction boundaries, present stamps, required future stamps/index writes, compensation and test pointers. Package rollback leaves wiki unchanged today; do not invent restore semantics. Unsupported namespace/source-locale/collection lifecycle remains explicit.
- [x] Record approved authoring contract: package owns qualified property schemas; explicit manifest declarations and future staff editor create validated fact sets; extraction is offline migration; local overrides use existing three-way package planning; schema changes require versioned migration. Provide proposed manifest example labelled non-executable; reject implication existing parser supports it.
- [x] Report measured Phase A outputs, exact baseline/test commands and limitations. Define Phase B tasks only after contract/oracle/corpus review: atomic store/write seam, partition catalog, bounded direct reads, revocation/recovery tests. No performance claims from oracle wall time. Maintain broader phases C/D pending in report.
- [x] Run normal engine test suite once after integration, exact solution formatting, default tool tests and full corpus acceptance; fresh whole-branch review. Fix findings, open reviewable PR linked #2031/#2016 (following closed organization review #1970) with bounded milestone description. Preserve artifacts/branches. Architecture review/load failures halt feature expansion, not privacy checks.

## Verification commands and ownership

Set `DOTNET_ROOT` to root `work/issue-consolidation/dotnet`, prepend its bin to PATH, and set all temp variables to a disk directory. Serialize dotnet commands with `flock --close /temp/codex/sharpmush-build.lock`; normal builds use `-m:2 -nr:false`. Root integrates shared project files. Filter TUnit with `/*/*/Semantic*/*` for focused runs, then run the engine project's normal command before completion. Tests exercising native Lightning do not require a live production server.

Review map: Tasks 2/3 consume Task 1 types, share no implementation files except the CLI entry point owned by Task 3. Task 4 is independent documentation + its own test file. Root publishes frozen contract signatures before parallel dispatch. Each task includes its own behavioral evidence; whole-branch review checks the handoffs and reports.
