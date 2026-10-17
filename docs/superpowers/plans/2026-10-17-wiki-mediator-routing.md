# Legacy wiki Mediator routing implementation plan

The user approved the isolated A–D semantic wiki prototype tracked in #2031. This is the independently reviewed B2 routing-first slice. It does not accept the outstanding B1 native cursor-error gate or activate semantic storage. Use superpowers:subagent-driven-development with isolated source ownership and independent review.

## Goal and selected composition

Every actual-host wiki store access from IWikiService traverses normalized data requests and Implementation handlers. Preserve page, revision, translation, author, rendering and authorization behavior. Preserve the single WikiStoreService(IWikiStore, WikiMarkdigPipeline) constructor and InMemoryWikiStore.CreateService for isolated fixtures.

Host composition is IWikiService -> WikiStoreService -> concrete MediatorWikiStore -> actual generated Mediator -> data handler -> provider IWikiStore. Register MediatorWikiStore only by its concrete type. IWikiStore remains the single provider registration. No handler receives the facade, adapter, Mediator or cache. No Library/Client dependency on Implementation is introduced. The current Client uses REST; its bUnit HTTP fixture's in-memory factory remains supported.

## Requirements coherence

New public data commands must invalidate warmed facade reads even when they bypass facade methods. Remove the facade's private requirements memo/version/ForgetRequirements. GetRequirementsAsync constructs WikiRequirements from the transport's current result. Only GetWikiRequirementsQuery uses existing ICacheable policy with a common WikiRequirements tag and Tagged profile. SetWikiRequirementsCommand and DeleteWikiPageCommand invalidate that tag through the existing pipeline. Other queries remain explicitly uncached; no manual FusionCache access, key-only removal, new invalidation mechanism or durable epoch claim.

This deliberately trades repeated pure requirements-wrapper construction for request-owned freshness. Offline fixtures reread their in-memory store. Host source requirements data remain cached by the existing tag/epoch contract. Record actual behavior and allocation costs; later B2 durable policy/requirements snapshots remain separate.

## Constraints and reviewed handoff

- Binding engine-data-trunk.md and approved semantic architecture remain authoritative. Update its section 2 wording to distinguish actual-host transport from standalone in-memory fixtures.
- The checked-in contracts are authoritative: `SharpMUSH.Library/Stores/IWikiStore.cs` defines the 25 store signatures and normalization contract; `SharpMUSH.Library/Queries/Database/Wiki/WikiQueries.cs` and `SharpMUSH.Library/Commands/Database/Wiki/WikiCommands.cs` define the corresponding 16 queries and 9 commands, including argument order, return types and cache policy. Recheck these sources before editing; do not add a parallel store interface or fake IMediator dispatcher.
- No semantic DI, tables, bootstrap, migrations, native code extraction, new world access, permission/author reconciliation, installer schema changes, endpoints, dependencies, production activation or deployment.
- Preserve all existing store method signatures and single-provider forwarding. Providers take no Mediator/cache. Requirements Tag invalidation follows the existing factory-start epoch pipeline; do not substitute item removal.
- Work in the current isolated checkout; coordinate Git/project changes with the integration owner. Preserve untracked work and prior branches. Create a writable disk-backed task scratch directory (use the workspace's `work/` directory if needed) and set `TMPDIR`, `TMP` and `TEMP` to it before running tools.
- Use an SDK satisfying the checked-in `global.json`. Serialize builds and test runs sharing output directories; coordinate a source freeze before formatting/build/tests. Restore dependencies using the repository's normal configuration when needed.

## Parallel task ownership

### A: normalized request transport and policy

Own Library/Queries/Database/Wiki/WikiQueries.cs, Library/Commands/Database/Wiki/WikiCommands.cs, Library/Definitions/CacheTags.cs, Implementation/Services/MediatorWikiStore.cs, Implementation/Handlers/Database/Wiki/WikiQueryHandlers.cs and WikiCommandHandlers.cs. All paths use the SharpMUSH. prefix project directory.

- [x] Recheck all 25 IWikiStore signatures and freeze positional request contracts against the checked-in query/command records.
- [x] Implement typed records and conventional handlers depending only on provider IWikiStore; map all parameters/results without normalization or new business rules.
- [x] Implement the concrete-only transport adapter over IMediator. Declare deliberate uncached policy on ordinary requests; use existing tagged cache/invalidation interfaces for the requirements trio.
- [x] Self-review mapping and cache policy, freeze source, and hand off exact signatures before the shared build. No wrapper-mirroring tests or deliberately failing compilation as behavioral RED.

### B: actual-host composition and behavioral acceptance

Own Library/Services/WikiStoreService.cs, Server/Registration/DatabaseRegistration.cs, new Tests/Wiki/WikiRequestRoutingTests.cs and narrowly necessary existing wiki test expectation changes. Do not edit A's files or project references.

- [x] Inspect real Server-generated Mediator and test-host configuration. Use actual handlers/provider; observe requests with established pipeline/decorator seams, never a fake dispatcher.
- [x] Write meaningful behavioral RED proving the old facade bypasses actual request dispatch and a warmed requirements result remains stale after direct normalized requirements commands/page deletion. Build current sources before running these assertions.
- [x] Switch explicit host factory/concrete adapter registration; retain provider IWikiStore registration and the facade's single constructor/offline factory.
- [x] Remove facade-local requirements authority memo. Keep normalization, rendering, raw author identities, source-locale guards and translation CAS behavior.
- [x] GREEN actual-host page/create/title/body/metadata/category naming/pins/requirements/source and translated history/translation CAS/delete journeys, proving the data handlers and persisted outcomes. Include direct requests after warm cache, factory/write race, failure invalidation and parallel resolution without DI recursion. Assert no semantic table initialization.
- [x] Preserve offline service/controller tests and the bUnit route wrapper. Update only expectations tied to intentionally removed memo reuse, documenting the cost.

## Root integration and review

- [x] Adopt and log selected composition/cache decisions, track task ownership and review, and dispatch disjoint A/B workers with frozen interface handoff and no concurrent builds.
- [x] Run normal formatting twice, normal build, focused new routing/cache tests, existing wiki/access/localization/controller/native wiki coverage and bUnit HTTP route tests appropriate to the changed composition. Keep exact commands/results and report any skipped tests separately.
- [x] Update binding trunk paragraph/XML for actual-host Mediator transport and standalone fixtures; do not claim all B2 semantic integration complete.
- [x] Independent task/whole-diff review, bounded correction and rereview.
- [ ] Create a separate ready PR referencing #2031; verify CI, address reviews and merge only with confidence under existing authorization. No issue closure for incomplete A–D gates.

## Verified local outcome

Normal Tests build: 0 warnings/errors. All 584 wiki tests and 8 cache-profile tests passed, with zero skips. The 10 bUnit route tests passed with zero skips; its build had 54 warnings in unchanged files and zero errors. The initial filename-based bUnit filter matched no tests and is excluded from this evidence. Actual-host routing, direct-write requirements invalidation, in-flight factory invalidation, exception-path invalidation and failure-path read drainage are covered. Independent transport and final scoped reviews passed. Semantic/native acceptance remains separate.
