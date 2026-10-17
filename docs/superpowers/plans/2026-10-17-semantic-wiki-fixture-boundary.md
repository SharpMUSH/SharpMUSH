# Disposable semantic wiki fixture boundary implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox tracking.

**Goal:** Admit only issued disposable worlds into the internal prototype composition and preserve their identity across fully closed host reopen.

**Architecture:** A provider-owned internal capability admits a unique fresh fixture and retains an exclusive lease for the real provider lifetime. Reopen corroborates existing Meta identity read-only before ordinary construction can write; the existing awaited migration binds first identity before seed writes. Test infrastructure owns the lineage and composes the normal singleton provider.

**Tech Stack:** Existing .NET 11 RC2, LightningDB 0.23.1, TUnit and ASP.NET test host; no new packages.

**Spec:** `docs/superpowers/specs/2026-10-16-semantic-wiki-architecture-design.md`; binding `docs/design/engine-data-trunk.md`; tracked by #2031.

## Global constraints

- User approval is for isolated A–D implementation. No production activation, environment switch, public semantic route, release or deployment.
- Ordinary public provider constructor and default DI remain unchanged. No semantic tables or default migration are introduced.
- Reuse the primary isolated branch `codex/semantic-integration-boundary`, based on the reviewed routing correction; preserve other branches and private evidence. Root owns Git and project files.
- Missing native mid-iteration error runtime proof remains required for final B1/Phase B acceptance. The approved specification gates Phase B as a whole; root permits this reversible internal construction slice while that proof remains open.
- Task 1 introduces no unused semantic operation stubs, public mutation DTOs or fixture-codec extraction. Those wait for a real Task 3 consumer. Legacy wiki operations remain legacy.
- No malformed native files, unsafe lifetime experiments, copied production worlds or second environment on an active world. Rejection tests may use only valid, closed test-owned fixture copies.
- Work in the current isolated checkout with an SDK satisfying `global.json`. Create a writable disk-backed task scratch directory (use the workspace's `work/` directory if needed) and set `TMPDIR`, `TMP` and `TEMP` to it before running tools. Serialize builds and test runs sharing output directories; coordinate a source freeze before formatting/build/tests. Restore dependencies using the repository's normal configuration when needed.

## Review focus

- Native construction creates named tables and commits: wrong/missing identity must be rejected before that constructor on reopen.
- A marker copied onto an unrelated valid closed world must not authorize writes; an issued in-memory handle and exact persisted identity are both necessary.
- Failed initial construction/migration cannot become implicit permission to adopt leftover files. Fail closed and retain unsafe-to-reuse state.
- Lease release must follow completed Store disposal; simultaneous hosts must never open the same LMDB environment path.
- Default/fixture host compositions must resolve the same real provider instance across lifecycle, wiki, package and storage access, retaining plugin and credential dependencies.

---

## Recommendation

Implement only fixture admission, actual-host composition, retained lineage lifetime, and explicit documentation that integrated semantic operations are unavailable and unexposed. Keep the public LightningDatabase constructor and ordinary DI unchanged. Defer public semantic DTO/store interfaces, PageSnapshot extraction, codecs, reader/credential contracts, and successful semantic writes to their actual consumers in Task 3 or later. Do not add a guard or fake semantic operation with no actual consumer; an exported CommitAsync signature with no settled integrated input model is premature.

The following file map is selected:

- New `SharpMUSH.Database.Lightning/Semantics/SemanticFixtureCapability.cs`: issued capability and exclusive active lease, private construction, marker validation and read-only reopen identity admission.
- New `SharpMUSH.Database.Lightning/LightningDatabase.SemanticFixture.cs`: internal provider factory, retained lease, fixture identity initialization helper.
- Existing `LightningDatabase.cs`: release retained lease only after Store.Dispose; preserve public constructor signature.
- Existing `Migration/LightningMigration.cs`: a fixture-only identity bind/check before its first seed write; ordinary provider path immediately returns without reads/writes. No semantic tables or migration registration.
- Provider csproj: add only Infrastructure friend access.
- New Infrastructure `SemanticWikiPrototypeFixture.cs` in Task A owns issuance and provider-test support; Task B adds `SemanticWikiPrototypeHost.cs` over that owner. All issuance callers live in Infrastructure. Root adds Infrastructure friend access for Tests so atomic Claim can be tested without opening a second environment.
- New `SharpMUSH.Tests/Wiki/Semantics/IntegratedWikiBoundaryTests.cs`: provider admission/lifetime/identity evidence. `SharpMUSH.Tests/Wiki/Semantics/IntegratedWikiHostBoundaryTests.cs` supplies actual-host composition evidence; `TestHostLifetimeTests.cs` supplies retained host lifetime evidence.

## Existing constraints that determine the design

`LightningDatabase.cs:25–59` takes logger, options, password service, explicit relation loader, optional migration sources/flags/credential gate/lazy revokers. Its Store field initializer opens LMDB immediately. `Store/LightningStore.cs:88–115,214–235` creates directories and commits table creation during construction. An overload that delegates to the existing constructor and validates its body afterward has already opened/written the selected world.

`Server/Registration/DatabaseRegistration.cs:122–151,196–225` registers one concrete provider and forwards ISharpDatabase, IDatabaseLifecycle, IWikiStore and ILightningStorageAccessor to it. The constructor receives the real relation loader, PluginCatalog migration sources/flags and credential dependencies. Preserve these dependencies; do not construct a separate fixture store or pass null relations in a real host.

`Server/Program.cs:83–92` awaits lifecycle migration immediately after Build. `Migration/LightningMigration.cs:29–44` currently begins seed writes inside MigrateCoreAsync. Put initial fixture identity binding before those writes and keep Program's existing awaited ordering. Do not migrate from a constructor, hosted service, or test-side duplicate migration call.

`ServerTestWebApplicationBuilderFactory.cs:25–32` already accepts explicit worldPath and a final ConfigureTestServices callback. `ServerWebAppFactory.cs:228–235,250–260` creates a fresh UsesOwnWorld directory and deletes it on disposal. That wrapper is unsuitable as the retained lineage owner without changes; compose the existing lower-level factory from a dedicated wrapper instead. No existing reusable semantic lineage factory was found in Infrastructure.

## Concrete internal signatures

The following internal signatures are selected. Use provider namespace ownership; no Library public API is needed. Add `Store/LightningStore.FixtureAdmission.cs` for the bounded read-only identity helper.

```csharp
internal sealed class SemanticFixtureCapability : IDisposable
{
    private SemanticFixtureCapability(/* private issued identity only */);
    internal Guid FixtureId { get; }
    internal string CanonicalPath { get; }
    internal static SemanticFixtureCapability IssueNew(string ownedScratchRoot);
    internal SemanticFixtureLease Claim(LightningStoreOptions options);
    public void Dispose(); // revoke issuance; refuse while a live lease exists
}

internal sealed class SemanticFixtureLease : IDisposable
{
    internal Guid FixtureId { get; }
    internal string CanonicalPath { get; }
    internal bool IsFirstOpen { get; }
    public void Dispose();
}

// In the LightningDatabase partial; existing public ctor stays unchanged.
internal static LightningDatabase OpenSemanticFixture(
    SemanticFixtureCapability capability,
    ILogger<LightningDatabase> logger,
    LightningStoreOptions options,
    IPasswordService passwordService,
    IObjectRelationLoader? relations,
    IReadOnlyList<IMigrationSource> migrationSources,
    IReadOnlyList<PluginFlag> pluginFlags,
    AccountCredentialGate credentialGate,
    Lazy<IEnumerable<IAccountCredentialRevocation>> credentialRevokers);

internal ValueTask InitializeSemanticFixtureIdentityAsync(CancellationToken ct);
```

The factory calls Claim *before* `new LightningDatabase(...)`, retaining the successful lease in the returned instance. Distinguish pre-native rejection from uncertain native construction. Pre-native rejection may release its claim. A successfully returned provider releases only after Store disposal completes. If construction throws without a returned provider, native closure is not proven: poison reuse and retain quarantine ownership/reservation; owner disposal must not delete potentially live artifacts. Do not broaden this task into Store.Open exception-safety refactoring. A failed close is likewise quarantined, never treated as successful release. Do not expose a public constructor parameter or environment flag that issues the capability. No FromPath/FromGuid/deserializer grants authority. An internal/private lease constructor prevents callers constructing one from its displayed GUID.

Do not add semantic operation stubs or a free-standing availability guard solely for tests. Task 1 exposes no integrated semantic read/commit API and must document those operations as unavailable/unimplemented. Existing legacy IWikiService operations remain legacy during this slice and cannot be advertised as successful semantic writes. Actual input/result contracts and runtime unavailable-to-success transition belong with the Task 3 consumer. This narrows the draft's unavailable-operation bullet honestly instead of fabricating operation evidence.

## Admission and lineage contract

1. Issue only a new unique child under a fixture-owned scratch root, using exclusive reservation/marker creation. Refuse pre-existing/nonempty directories; never accept an arbitrary world path as issuance. Use disk-backed work storage, never /tmp. Canonicalize once, reject linked/reparse fixture paths, and store schema version, canonical path and fixture GUID in the marker. GUID uniqueness alone is not an exclusivity check.
2. The issued in-memory object is the authority. Claim requires its live unreleased identity, exact canonical options.Path match, exact marker version/path/GUID, and no active lease. A copied marker in another directory fails path/handle checks. Disposal revokes future claims.
3. First open admits only the marker/reservation in the empty directory, before invoking the constructor. Bind that same fixture GUID as an exact 16-byte primitive in existing Meta under a fixture-specific key (the issued handle and marker already bind its canonical path) at the beginning of awaited migration. This is fixture identity, not semantic schema activation. No SemanticTables are opened in either ordinary or fixture Task 1 hosts.
4. Reopen requires the same issued object after the previous host/store is fully disposed. Require the persisted provider Meta identity too. Missing/mismatched Meta is rejection, never permission to initialize an already existing world. This catches an unrelated valid world placed under the old marker/path.
5. Crucially, verify reopen Meta **before calling the normal provider constructor**: it writes during Store.Open. Only after obtaining the exclusive lease on the owned, closed fixture, use a short read-only LMDB environment and read-only transaction, opening the existing Meta database without Create; close both before constructing the actual provider. Existing supported pattern: `SharpMUSH.Tests/Database/Lightning/CascadeCoverageTests.cs:43–48`. Require native identity value length exactly 16 before copying/decoding. Put the small read-only identity helper in the LightningStore partial so it can reuse the reviewed B1 native size binding without extracting a reusable codec. No malformed-native-input experiments are needed. This temporary identity check provides no semantic read/write authority. Recheck identity in the migration helper before seed writes as defense against accidental lifecycle misuse.
6. A first initialization failure before OR after identity binding leaves an incomplete fixture that cannot reopen. Acknowledgment occurs only after the entire first public Migrate completes, including credential-gate entry, seeds, plugins and ReloadDefinitions. Cancellation before the identity helper, migration failure after binding, and close without first migration poison reuse. After proven native closure, revoke/remove this owned incomplete fixture and issue a fresh one; uncertain closure stays quarantined. A later unrelated host-startup failure after successful initialization may reopen only after full closure is proven. Recovery of partially initialized fixtures and child-process capability transfer are separate later work. A child must not gain authority from path/GUID command-line values alone.
7. Trusted fixture orchestration must not replace the directory while a claim/open is in progress. This is a test ownership boundary, not protection against a malicious same-OS-user copying/replacing files between checks. Do not claim path+GUID cryptographically authenticates file contents. Wipe/promote/import is outside this fixture lineage contract; do not call it in these fixtures or silently reactivate a replacement world.

## Host composition and exact lifecycle

Use a public test-infrastructure wrapper (public only because Tests consumes Infrastructure), keeping its capability private:

```csharp
public sealed class SemanticWikiPrototypeHost : IAsyncDisposable
{
    public SemanticWikiPrototypeHost(string ownedScratchRoot,
        string sqlConnectionString, string configFile,
        INotifyService? notifier, string sqlPlatform = "mysql");
    public IServiceProvider Services { get; } // throws while closed
    public Task OpenAsync();                // first open or same-handle reopen
    public ValueTask CloseAsync();          // disposes host; retains issuance
    public ValueTask DisposeAsync();        // close, revoke, remove owned artifacts
}
```

Task A first introduces `SemanticWikiPrototypeFixture(string ownedScratchRoot)`, an Infrastructure-owned IAsyncDisposable utility with public WorldPath/FixtureId, internal Capability for friend Tests and OpenProvider forwarding the explicit factory arguments. Unit fixtures may pass null relations explicitly, consistent with the existing provider contract; the real host must resolve the actual loader. Task B host constructor owns this utility rather than issuing directly. Open composes `ServerTestWebApplicationBuilderFactory<Server.Program>(sqlConnectionString, configFile, notifier, sqlPlatform, capability.CanonicalPath, ConfigureFixtureServices)`. Keep existing TUnit dependencies/NATS setup alive around this wrapper; do not introduce a second environment-global test configuration mechanism. Close must await full host disposal; concurrent opens are refused. Retain the failed-start factory reference until disposal is observed. The pinned lower factory records its host only after startup succeeds, while the server RunAsync path independently disposes late failed starts. Prove closure at the actual failure boundary rather than inferring a missing owner from the factory alone. Managed normal-close, late-start and pre-RunAsync failure tests must check the Store disposal guard before any raw environment inspection or reopen. If an actual failure leaves a constructed host unclosed, retain and explicitly close that owner inside Infrastructure while preserving the lower factory behavior. Tests independently retain every constructed host/provider for guaranteed finally cleanup; they cannot orphan a native owner while demonstrating a defect. Publish Services only after successful Open. Dispose is terminal and cleans only proven-closed owned artifacts; quarantine retains the path/reservation with an explicit diagnostic. Engine lock/key-ring/backup siblings require explicit ownership and full host closure before cleanup. Before touching the factory, reject pending import-operation directories or conversion invocation for this lineage: Program acquires the engine lease and applies pending import promotion before provider resolution. Do not refactor production imports or pretend the provider guard prevents that earlier operation.

The common lower test factory must join the actual host's full Stop/Dispose completion. Pinned ASP.NET factory disposal races Program's RunAsync disposal, and the default DI scope returns immediately to a second DisposeAsync caller while its first asynchronous disposal is still running. Use a test-owned IHost proxy registered as an ImplementationInstance, preserving the original IHost factory for concrete inner construction. Bind its exact root provider through the preserved IConfiguration factory: pinned HostBuilder.ResolveHost resolves configuration before IHost. Guard unexpected descriptor shapes, foreign rebinding and premature use; treat a hosting upgrade as a composition review. Publish shared Start/Stop/Dispose tasks before beginning their operations, with no lock across awaits, so concurrent callers and children spawned inside disposal join actual inner completion and observe the same failures. The proxy is externally owned and absent from the container's disposal list, avoiding DI self-disposal recursion without AsyncLocal inference. The lower factory retains the proxy for normal and failed startup and awaits it even when the base factory never published a host. The semantic wrapper delegates lifetime correctness to this common owner; no duplicate failed-only Stop path. Deterministic managed barriers cover in-flight stopping/disposal, inherited-child callers, failure preservation and binding guards, alongside actual host Store closure proofs.

The final services callback replaces only the concrete LightningDatabase descriptor with the internal factory. Resolve real ILogger, IPasswordService, IObjectRelationLoader, AccountCredentialGate and PluginCatalog; pass catalog.MigrationSources/AllFlags and `new Lazy<IEnumerable<IAccountCredentialRevocation>>(() => sp.GetServices<IAccountCredentialRevocation>().ToArray())`. Supply explicit full-sync fixture options with the issued path. Do not execute ordinary compaction options on a capability fixture. Leave ordinary interface forwarders intact; they resolve the replacement singleton. Do not register a preconstructed provider whose ownership/disposal the DI container would not track.

Reference assertions must compare resolved LightningDatabase against ISharpDatabase, IWikiStore, IDatabaseLifecycle, IPackageRegistryService and ILightningStorageAccessor. Exercise real wiki/command/package compositions to ensure no fixture substitute bypasses those forwarders; package installer itself is a service, not a provider instance to compare. Host identity-by-reference is necessary evidence; equal strings/options are insufficient. No Mediator or cache goes into the provider, per binding data trunk.

## Behavioral RED/GREEN sequence

- Begin with existing compiling host/provider APIs: assert ordinary host identity and no semantic named databases. These are baseline invariants and may already be GREEN; do not call them regression RED.
- Add compiling internal capability/factory declarations and the fixture composition seam, initially leaving the positive fixture composition behavior unimplemented or rejection checks absent behind a compiling seam. A positive issued-empty-fixture open/reopen test should then fail at runtime for the missing behavior. A type-not-found compiler error is scaffolding work, not behavioral RED.
- Introduce focused failing assertions for mismatched options path, copied marker at another path, unrelated valid fixture world with copied marker, absent/mismatched persisted identity, simultaneous leases, disposed handle and reopen after full close. Negative tests can initially pass under blanket rejection; they are not evidence of positive admission. Record each actual observed RED honestly.
- For admission RED, use a valid unrelated owned fixture database with an absent/different identity under the expected marker, and assert rejection before native construction changes its tables/rows; a guardless baseline must fail that runtime assertion. Also record behavioral rejection tests for an active lease and marker-only/copied-marker attempts. Test atomic Claim alone for the guardless active-lease RED; never construct a second environment on the live fixture. All raw Meta/catalog inspection occurs after full close or through the existing live Store transaction, never another environment. Assert no semantic named databases and no legacy row changes from rejected admission. Inspect the native root catalog with a read-only transaction when needed; opening a semantic table to check whether it exists would create it and invalidate the test.
- Task 3 adds the actual integrated IWikiService operation and coherent legacy/semantic receipt assertion, adds its real compiling unavailable implementation and observes runtime failure against that boundary, and implements its success. Do not freeze speculative read/credential contracts merely to make Task 1 compile.

This keeps Task 1 reviewable and gives Task 3 a real provider/lifetime boundary. It does not establish semantic mutation atomicity, query correctness, package compensation, bootstrap recovery, or final Phase B acceptance.

## Task A — provider admission and lease lifecycle

Own provider capability/factory, Store read-only identity helper, provider disposal/migration hooks and provider boundary tests. Root owns provider/Infrastructure csproj friend changes. Task A also owns the minimal Infrastructure fixture owner; Task B later composes it. Existing public constructor signature stays unchanged.

- [x] Inspect the selected signatures and actual constructor/migration/native access patterns. Add minimal compiling factory scaffolding plus real provider admission tests. Record runtime behavioral RED for missing positive admission or a guardless negative path; compile failure is not RED.
- [x] Root runs the frozen RED build/test and records the exact failure. Even a failing rejection assertion must dispose any mistakenly admitted provider before returning; no lost native handles.
- [x] Implement issued marker, exclusive lease, pre-constructor validation and bounded read-only Meta corroboration. Marker version is 1 with canonical path and GUID; persist the GUID as exactly 16 big-endian bytes under Meta key `prototype.semantic.fixture.identity/v1`. Require the Meta database's ordinary nonduplicate flags.
- [x] Issue a unique child through an exclusive reservation plus reject-existing-directory protocol; GUID randomness alone is not exclusivity. Trusted orchestration respects reservations and never replaces paths during admission. This is not protection against a malicious same-OS-user.
- [x] Bind identity before seed writes, but acknowledge first initialization only after whole public Migrate returns successfully. Add actual failing managed IMigrationSource after bind and cancellation-before-entry/close-without-migration cases; dispose provider completely, then prove same-handle reopen is refused. Reopen never creates missing identity. Uncertain construction/close retains quarantine, not a released lease. Test managed quarantine ownership directly without creating an unsafe native partial-construction fault.
- [x] Root formats twice, normal-builds, runs the complete boundary class and appropriate existing Lightning lifecycle/cursor/bounded-read tests. Independent source/runtime review precedes root commit.

## Task B — real retained host composition

Depends on frozen Task A signatures and review. Own Infrastructure wrapper and actual-host cases in the same boundary test file after Task A releases it. No parallel edits to shared files.

- [x] Write actual-host RED for missing retained fixture open/close/reopen behavior. Use the existing lower-level test host and real service registration, not a fake provider or dispatcher. Root runs RED before the composition fix.
- [x] Replace only concrete provider construction; preserve every interface forwarder, real relation loader, PluginCatalog sources/flags and credential gate/lazy revokers. Only Infrastructure issues capabilities.
- [x] Prove reference identity across provider interfaces and exercise a legacy wiki write/read plus a real command/package consumer through the same actual host. No claim that this is a semantic mutation.
- [x] Prove close fully disposes storage while retaining issuance, reopening preserves valid rows/identity, concurrent open and use-after-owner-disposal fail closed, and cleanup touches only owned artifacts. Inspect named databases read-only and prove no semantic table creation in default or fixture hosts.
- [ ] Root normal-builds and runs actual-host boundary and existing wiki/provider lifecycle regressions. Run appropriate complete CI, review/respond/resolve and merge only with confidence; keep #2031 open.

## Verification and completion limits

Tests use distinct TUnit filters: `/*/*/IntegratedWikiBoundaryTests/*` for provider admission, `/*/*/IntegratedWikiHostBoundaryTests/*` for actual-host composition, and `/*/*/TestHostLifetimeTests/*` for retained host lifetime. They exercise actual provider/host behavior. Do not add tests that merely mirror signatures or assert source text. Cancellation/failure paths must drain all started tasks before disposing synchronization objects or storage. Record baseline green separately from actual RED/GREEN and report skipped tests. Whole-branch review is required.

This plan completes only disposable fixture admission/composition. It cannot complete B1 runtime fault proof, atomic integrated mutations, authority certificates, installer compensation, queries, generation rebuild or performance acceptance. No child-process capability transfer is implemented; it needs an explicit later orchestration protocol.
