# SharpMUSH remote analysis, plugins and editor implementation plan

> **For agentic workers:** Use superpowers:executing-plans to execute this plan in reviewed work packages. Subagent-driven-development is an alternative only with explicit delegation authorization. This document authorizes no execution, publishing, credential setup or deployment.

**Goal:** Let builders connect editors and MCP clients to a Docker-hosted game for safe analysis using its live catalog, retain offline analysis, and prepare explicit conflict-safe attribute sync.

**Architecture:** Reusable analysis/catalog contracts; authoritative host identity and permissions; separate optional MCP and remote LSP plugins; first remote editor delivered by an MCP adapter. Transport sessions are isolated and bounded. Future writes pass through permissions, Mediator and atomic stores.

**Tech Stack:** Existing pinned .NET 11 RC, TUnit, ASP.NET Core, SharpMUSH plugin loader, current MCP SDK and OmniSharp LSP stack, TypeScript VS Code extension, existing Docker/Caddy deployment patterns. Use existing dependencies before adding any.

**Spec:** [Proposed scope](../specs/2026-10-11-remote-code-tools.md). Owner goals are established; proposed contracts, limits and transport choice await review.

## Global constraints

- Work in isolated checkout `/home/grave/Documents/Codex/2026-10-11/task-3/SharpMUSH`, branch `codex/lsp-mcp-baseline`; preserve source checkout changes. Do not commit, publish a PR, merge or deploy without the agreed scope and authorization.
- No live game, firewall, credentials or public service changes. Disposable fixture credentials must remain ephemeral and require the future fixture's explicit approval. Do not install system software or change OS trust/hosts files.
- Follow root/repository AGENTS.md and `docs/design/engine-data-trunk.md`: game reads/writes use service permissions and declared Mediator requests/handlers/stores, not direct providers or new static service holders.
- Host transport extraction must preserve default-off production behavior and explicit character authentication; development DebugAuth must never grant MCP/LSP access.
- No code evaluation or persistent writes in the analysis slice. Bound input before parsing; cancel obsolete work; redact credentials and document contents from logs.
- Every issue adds a failing behavioral test first, implements the smallest change, runs its focused checks, and records evidence. Commits below are proposed review boundaries, not commands to commit now.

## Review focus

Watch host/plugin type identity and publish dependency closure; world readiness versus plugin registration order; permission-filtered metadata; stale asynchronous editor results; finite work for adversarial documents; proxy forwarding trust; UTF-16 ranges; literal whitespace preservation; and atomic conflict protection. Existing tests that construct handlers directly do not prove the actual entrypoint or published plugin works.

## 1. What is established today

The actual stdio host originally failed on eager runtime service resolution; after that was corrected, it failed on an options wrapper and lacked built-in metadata. Local fixes defer evaluation-only services, use default options, and generate metadata from the same attributes as runtime bindings. Built-in libraries now populate without constructing a game. A reproduced variadic signature loop for `ADD` is bounded. The actual process passes 42 checks including initialization, valid/invalid documents, five extensions/modes, mixed-case intelligence, aliases, variadic signatures and shutdown. Focused tests pass 75/75; builds have no warnings/errors. These local changes remain uncommitted.

MCP currently lives in `SharpMUSH.Server/Mcp`, uses Streamable HTTP with `Stateless=true`, character Basic authentication, eight analysis/document tools, and a singleton document store. Its global handles have no principal ownership, TTL or byte bound. LSP currently offers stdio only. `/ws` is the game terminal. The example extension launches a local DLL and does not connect to a remote game. The portal has separate/static metadata logic. Attribute PUT supplies a value without an expected revision.

Existing MCP tools: validate, format, hover, complete, signature_help, document_symbols, open_document and close_document. Do not imply definitions, references, rename, semantic tokens, inlay hints or game writes exist through MCP.

## 2. Architecture and proposed contracts

### Shared analysis boundary

Start by extending `SharpMUSH.CodeAnalysis` rather than creating a shared analysis plugin. Keep transport-neutral results and `IMushCodeAnalyzer` there, with a narrow catalog interface. If consumers cannot share that assembly without implementation dependencies, split a small `SharpMUSH.CodeAnalysis.Contracts` project only after a dependency test proves the need; do not preemptively redesign every project.

Proposed concepts, with final names settled in issue 2:

| Contract | Required semantics |
| --- | --- |
| `AnalysisRequest` | Code, explicit parse mode, document URI/version if present, cancellation. No execution context that permits evaluation. |
| `AnalysisIdentity` | Host-supplied world identity, character full DBRef incarnation and account identity. Transport cannot manufacture this from client parameters. Offline identity is explicitly absent. |
| `CatalogSnapshot` | Opaque world/process epoch + revision, immutable entries, origin, canonical name and aliases, bounded arity/variadic marker, argument names, flags, switches and permission-visible restrictions. No runtime delegates or attribute values. |
| `IAnalysisCatalog` | Obtain a principal-filtered consistent snapshot; compare revision; optional change notification implemented by the host. Offline implementation supplies generated built-ins/default aliases. |
| `AnalysisCapabilities` | Supported modes/features, position encoding, input limits, schema version, offline/live source and catalog revision. Explicitly describes unavailable operations. |

Keep generated `CreateMetadata()` additive and runtime binding signatures compatible. Do not represent infinite arity by generating infinite parameter arrays. Preserve case-insensitive names, alias precedence and owner-local user-function scope. Avoid leaking unavailable local functions through a global catalog. Distinguish metadata discovery from permission to execute a listed function.

Use UTF-16 editor positions consistently with existing LSP clients; validate line/character boundaries and supplementary Unicode cases. Decide whether malformed positions produce a protocol error or empty result and use one policy across transports. Do not silently reinterpret functions as commands based on extension alone when a request supplies an explicit supported mode.

### Plugin boundaries

Proposed new projects: `SharpMUSH.Plugins.Mcp` and, later, `SharpMUSH.Plugins.Lsp`. Each has an entrypoint, `plugin.json`, registration/options, endpoints and transport-specific state. Use `IServiceRegistrar` before host build and `IEndpointContributor` afterward. Shared services resolve from request/root DI; mapping must not rely on plugin Initialize side effects, since initialization and hosted-service startup occur later.

The manifest can share `SharpMUSH.CodeAnalysis` through existing `sharedAssemblies`; its interface is not in `PluginLoader.SharedContractTypes`, and adding an implementation-dependent interface there creates a cycle. Test published type identity instead of assuming matching assembly names suffice. Host library references use the existing Scene `Private=false`/runtime exclusion pattern. Package the MCP/LSP SDK dependency closure; Scene's three-file copy is not sufficient evidence for a transport plugin's private dependencies.

Keep credential verification and permission services in the host. Define an explicit host-owned named policy for analysis transports in a host-neutral existing contract location; plugins reference its name without referencing the Server project. Retain explicit `MushBasic` behavior initially. General authentication/rate-limit/forwarded-header middleware stays in the host. Plugin rate/connection policy and endpoints remain optional.

An LSP plugin can build an endpoint-local branch using the installed ASP.NET `IEndpointRouteBuilder.CreateApplicationBuilder()` and `UseWebSockets()`, then map its built delegate under outer authorization. A new general middleware plugin hook is not required by those APIs. Prototype this route and test authorization before assuming pipeline ordering. Never attach WebSocket middleware globally just to make optionality appear simpler.

Plugins are boot-time optional: missing/disabled plugins require restart to change availability. Service-registrar/endpoint plugins are not hot-unloadable today. Registration/map failures must fail closed and appear in readiness/operator logs; existing exception logging alone is not transactional rollback.

### Transport decision

| Option | Strength | Cost / limitation | Recommendation |
| --- | --- | --- | --- |
| VS Code MCP adapter | Reuses current HTTPS transport; extension Node runtime; smallest path to Docker game analysis | Maps only existing tool features; not generic LSP; per-request overhead | First remote vertical slice |
| Authenticated WebSocket LSP + bundled stdio bridge | Full LSP protocol and non-VS Code client route; live per-session docs | New endpoint, framing, lifetimes, bridge packaging and reconnect semantics | Second stage when full LSP is required |
| HTTPS catalog + local stdio analyzer | Local latency and existing host | Duplicates runtime/version and may diverge from live analysis | Optional offline/cache assistance, not primary live path |
| Raw TCP LSP / SSH tunnel | Simple streams in controlled setups | Additional networking/client operational burden | Do not introduce a public raw TCP listener |

For WebSocket, propose one UTF-8 JSON-RPC object per text message; reject binary messages and oversized fragmented payloads. The bridge translates stdio Content-Length framing to/from WebSocket messages. Confirm OmniSharp input/output adaptation with a small prototype before choosing a stream wrapper or custom reader/writer. Do not send arbitrary game-terminal frames or claim standard LSP defines this transport. Preserve ids, notifications, cancellation and shutdown/exit order; no multiplexing principals in a single session.

Basic auth requires clients that can supply an Authorization header. Do not promise interoperability with MCP clients supporting only OAuth discovery. Future OAuth is a separate host auth feature; no credentials in URL/query string or workspace settings. VS Code stores secrets in SecretStorage and sends credentials only to the explicitly selected HTTPS origin, including redirects policy.

## 3. Security, resources and lifecycle

Proposed initial configurable limits below are sizing hypotheses to validate in fixtures, not current behavior or established safe production values:

| Resource | Initial proposal |
| --- | --- |
| Document UTF-8 text | At least 5 MiB each by default, configurable; report rejection before parsing |
| Per editor/LSP session | Configurable document count and total bytes accommodating a 5 MiB document; concurrency chosen after stress benchmarks |
| Per principal temporary MCP documents | 64 documents, 16 MiB; handles tied to world/process/principal |
| Global stored text | 128 MiB hard cap, deterministic admission/eviction policy |
| Connections | 8 per principal; operator-configurable server cap |
| Analysis deadline | 5 seconds plus cancellation; benchmark longest reasonable input |
| Diagnostics debounce | 250 ms in client, immediate explicit request |
| Temporary handle idle TTL | 15 minutes, bounded cleanup sweep; close idempotent |
| Remote session revalidation | On revocation event plus periodic check; exact cadence set by auth model |

Reject over-budget requests rather than parsing then evicting unrelated users. Account for JSON/transport overhead separately from text budget, and cap incoming body/message size before full allocation. Stateless MCP transport does not eliminate state in open_document. Handle ownership includes full character incarnation/account identity; guessed handles are denied without existence disclosure. Restart invalidates handles by process epoch. Specify expiration/eviction errors compatibly; old unowned in-memory handles cannot be migrated safely and expire on restart.

Retain current `mcp` 300/minute per-IP limiter and Retry-After while adding principal/work bounds. Trusted forwarding must resolve client identity; don't let a forged X-Forwarded-For bypass limits. WebSocket upgrade limits alone do not bound established session traffic. Cancellation has to reach parsing/analysis or work must be isolated/bounded; merely cancelling the HTTP response is not protection from continued CPU work.

HTTPS terminates at the existing proxy. Verify forwarded scheme/client IP against only known proxy addresses. MCP validates Origin according to the transport requirements. WebSocket accepts authenticated intended clients and configured browser origins; absent Origin for authenticated non-browser clients is an explicit policy choice. Never use wildcard origin plus credentials.

Disconnect releases buffers/docs, cancels work and stops notifications. Reconnect creates a new server session and resends current documents; old handles are not trusted. Account/character revocation and incarnation changes invalidate authorization and close affected sessions. If the host has no revocation event, define the maximum periodic revalidation window and disclose it; do not pretend an established socket reauthenticates on every message.

## 4. Delivery phases and dependency order

1. **Phase A — verified foundation:** issues 1–2. Review existing repairs, shared contracts/ranges and safe feature declarations. No remote deployment.
2. **Phase B — secure optional MCP:** issues 3–5 in that order. Principal-owned bounded documents and named policy, extraction/publishing, disposable HTTPS fixture.
3. **Phase C — minimal remote editor:** issue 6 plus catalog refresh subset of issue 7. Prove live game-backed diagnostics/intelligence without world writes. This is the first releasable remote journey.
4. **Phase D — protocol depth:** issue 8 and full issue 7. Remote LSP/bridge when desired; full capability tests and safe formatting/refactor decisions. Portal reuse follows contract stability.
5. **Phase E — explicit data sync:** issue 9 only after identity/revision/permission decisions. Pull first; conditional push later.
6. **Phase F — operator rollout:** issue 10 accompanies every release, with opt-in activation and rollback acceptance. No deployment in this planning task.

Issue 7's catalog revision contract starts in issue 2; built-in/plugin catalogs are required for phase C. Permission-filtered user-defined functions and portal migration can be later sub-issues. Issue 8 does not block phase C. Issue 9 must not be used as a reason to expand analysis privileges.

## 5. Concrete issue breakdown (drafts, not posted)

### Issue 1 — Preserve and review the reproduced offline host repairs

**Files:** existing modified parser/evaluation-service files, both implementation generators, LSP Program and BuiltInAnalysisLibraries, analyzer intelligence, `SharpMUSH.Tests.Integration/LanguageServer/StandaloneParserTests.cs`, `SharpMUSH.Tests.Integration/LanguageServer/BuiltInMetadataTests.cs`, `scripts/test-lsp-stdio.py`, LSP README. Confirm exact tracked paths before staging.

- [ ] Review current diff against `e40ed912`; verify no user-source changes were copied or overwritten.
- [ ] Keep metadata generated from existing attributes and evaluation-only service construction lazy. Retain runtime registration semantics.
- [ ] Run the actual stdio harness against rebuilt standalone DLL; require 42/42, successful shutdown and no persistent process.
- [ ] Run metadata parity/independence, standalone parser, CodeAnalysis and LanguageServer focused tests; compare baseline logs.
- [ ] Record zero-warning builds, format checks and `git diff --check`; proposed commit: `Fix standalone LSP setup and generated built-in metadata`.

Acceptance: real process works without a game, aliases/case/variadic metadata match runtime, and the normal evaluation path still resolves runtime services when needed. Existing full integration evidence predates the final metadata work: run an approved disposable runtime test before treating it as a runtime regression signoff.

### Issue 2 — Define shared catalog/capabilities and correct unsafe output boundaries

**Files:** existing `SharpMUSH.CodeAnalysis/{IMushCodeAnalyzer,AnalysisResults,MushCodeAnalyzer.Intelligence}.cs`; new `Catalog/{AnalysisCapabilities,CatalogSnapshot,IAnalysisCatalog}.cs`; LSP handlers and offline BuiltInAnalysisLibraries; existing `Library/Services/LibraryService.cs`; new focused catalog/range tests under `SharpMUSH.Tests.Integration/CodeAnalysis`.

- [ ] Write failing tests for immutable snapshot, alias/case precedence, variadic arity, UTF-16 supplementary characters, invalid positions and diagnostics ranges contained in the document.
- [ ] Introduce only transport-neutral contract types needed by MCP/editor; keep old analyzer APIs compatible through adapters initially.
- [ ] Supply offline generated catalog and live built-in/plugin catalog using library versions. Read version before/after snapshot or add a consistent snapshot operation; bound retries under churn.
- [ ] Add an epoch so revisions cannot be confused after host restart/world change. Capability response explicitly omits unimplemented features.
- [ ] Write literal-preservation formatting fixtures: ordinary spaces/tabs, multiline strings, `%r` text and nested code. Until a correct formatter passes, mark formatting unavailable in the new adapter; don't quietly rewrite semantics.
- [ ] Run focused tests and stdio harness, then proposed commit: `Introduce shared analysis catalog and capabilities`.

Acceptance: clients can distinguish offline/live data and unsupported features, snapshot mutation cannot alter runtime metadata, and position/results are valid. Do not expand to an all-engine refactor.

### Issue 3 — Harden analysis identity and temporary document lifecycle

**Files:** existing Server authentication handler/registration, `Server/Mcp/{MushTools,McpDocumentStore,McpOptions}.cs`; existing host-neutral contracts/policy constants location chosen without Server dependency; new security/resource tests next to current MCP integration tests.

- [ ] Add failing two-principal tests: open/get/close guesses denied; same numeric DBRef with different incarnation denied; inactive account denied; DebugAuth cannot satisfy transport policy.
- [ ] Add failing byte/document/global caps, concurrent admission, TTL expiration, duplicate close, eviction errors and cancellation tests with a fake clock and small configured limits.
- [ ] Pass trusted principal context to document operations; make quota accounting and ownership changes atomic. Never accept owner identity in a tool argument.
- [ ] Add explicit named host policy reusable by plugins, preserving current character Basic verification. Keep credentials out of logs/error bodies.
- [ ] Bound request body and analysis work, preserving current per-IP limiter. Declare residual cancellation gaps if parser APIs cannot yet stop work.
- [ ] Run current auth/MCP tools and new adversarial tests; proposed commit: `Isolate and bound MCP document state`.

Acceptance: principal A cannot learn or alter B's docs; limits hold under concurrent opens; valid existing inline-code clients behave unchanged. This issue must precede enabling remote document handles.

### Issue 4 — Extract MCP into a shipped optional plugin

**Files:** new `SharpMUSH.Plugins.Mcp/{McpPlugin.cs,plugin.json,SharpMUSH.Plugins.Mcp.csproj,Mcp/*}`; existing `Server/Program.cs`, `Server/Registration/WebHostRegistration.cs`, Server csproj, solution and appsettings; plugin tests. Inspect exact current registration filenames before edits.

- [ ] Write failing published-plugin smoke tests covering missing plugin, disabled state, config false, config true, private SDK dependencies and contract type identity.
- [ ] Move MCP registration/endpoints/tools/options/state into plugin without changing tool schemas/path/Basic policy. Preserve compatible `Mcp:*` configuration keys and default false.
- [ ] Remove direct SDK/runtime MCP coupling from Server only after the published plugin resolves all dependencies. Keep host analysis, identity and generic web middleware.
- [ ] Package full dependency closure using build/publish targets consistent with existing Scene, without duplicating host contract assemblies.
- [ ] Assert endpoint absence cannot masquerade as SPA HTML success. Assert readiness/logs reflect load failures and no unauthenticated partial endpoints remain.
- [ ] Repeat existing MCP protocol/auth tests against loaded plugin; proposed commit: `Ship MCP as an optional boot-time plugin`.

Acceptance: host/offline LSP boot without transport plugin; enabled plugin has old endpoint behavior; disabled plugin has no operational route. No changes to unrelated Scene loading or hot unload.

### Issue 5 — Build a disposable Docker/proxy connectivity fixture

**Files:** new `scripts/fixtures/code-tools/{compose.yaml,Caddyfile,README.md}` or existing test-container harness extension; new integration tests for published transport. Reuse cached Podman images/current harness rather than requiring software installs.

- [ ] Write a test that starts the actual production entrypoint, not an in-memory substitute, inside a unique private network with temporary world/NATS state.
- [ ] Put Caddy and a test client in that network; private DNS alias and fixture CA trusted only by client process. Publish no host/public ports. Fake characters are created by the harness only after fixture approval.
- [ ] Verify HTTPS forwarding, Basic denial/success, unknown/malformed Origin, MCP initialize/protocol-version headers, POST Accept JSON/SSE, tools/list and tools/call, GET behavior in stateless mode, limiter and Retry-After.
- [ ] Verify only configured proxy forwarding is trusted and HTTPS redirect does not loop. Verify account disable and store isolation through the actual route.
- [ ] Label every resource; collect redacted diagnostics; cleanup in finally, including failed startup and test interruption. Assert no residual containers/volumes.
- [ ] Proposed commit: `Test optional MCP through disposable HTTPS proxy`.

Acceptance: private realistic network demonstrates Docker/proxy/MCP works with published plugin; failures leave no persistent access. TLS fixture must not weaken production certificate validation.

### Issue 6 — Package the first remote editor journey using MCP

**Files:** existing `LanguageServer/vscode-extension-example/{extension.ts,package.json,README.md}`; new `tsconfig.json`, `src/{connection,mcpClient,analysisAdapter,documentModes}.ts`, tests and packaging configuration. Choose promoting this example versus a separate maintained extension before publication; initial branch can improve the example without publishing a marketplace identity.

- [ ] Add failing adapter tests for explicit mode mapping on all five extensions, UTF-16 positions, diagnostic document versions, stale responses, reconnect, auth failure and URL redirects.
- [ ] Add settings/commands for HTTPS game URL and explicit remote/offline mode. Store character credentials only in SecretStorage. Display connection identity/world/source and concise failure states.
- [ ] Use protocol-aware MCP client handling both JSON and SSE responses. For first diagnostics/intelligence use inline code to avoid handle lifecycle dependence; debounce, cancel and reject stale results.
- [ ] Map validate/hover/complete/signature tools into VS Code providers. Function signatures only where supported; commands must not show fabricated function signatures. Add symbols/formatting only after their tests pass.
- [ ] Show unsupported capability clearly; never silently replace live analysis with offline results. Offline fallback runs the actual corrected stdio host and identifies generated default catalog/runtime requirements.
- [ ] Build an installable extension artifact, including TypeScript compilation and all five language associations. Test from a clean extension host with no checkout-specific absolute DLL path. Remote mode needs no .NET install.
- [ ] Run private fixture end-to-end: edit valid/invalid code, builtin/plugin completion, disconnect/reconnect and two users. Proposed commit: `Add remote game analysis to editor adapter`.

Acceptance: a builder installs one packaged extension and connects to the private HTTPS game for analysis with no game writes. Offline mode remains explicit and usable. No rename/references/definition/semantic-token promises through MCP.

### Issue 7 — Refresh catalogs safely, then unify portal consumers

**Files:** `Library/Services/{LibraryService,UserDefinedFunctionService}.cs`, `Interfaces/IUserDefinedFunctionService.cs`, engine registration/catalog adapter, MCP capabilities/catalog discovery, LSP notification adapter, VS Code catalog cache; later Client Monaco integration files found by source search.

- [ ] Add failing revision tests for plugin add/remove where supported, alias/arity/flags changes, user-function define/disable/delete/restriction/local-owner changes and restart epoch.
- [ ] Add permission-visibility tests for global versus local functions and changed ownership/restrictions. Do not return `All()` directly to a remote principal.
- [ ] Add explicit revision/change support where current user-function registry lacks it. Use immutable consistent snapshots; account for principal-specific visibility so shared cache cannot leak entries.
- [ ] Define refresh behavior: MCP discovery/revision polling with bounded cache TTL initially; LSP refresh on catalog change and subsequent analysis. Invalidate on reconnect/world/principal change.
- [ ] Test catalog mutation during in-flight analysis and invalidate stale results. Report source/provenance and plugin disabled/unavailable metadata honestly.
- [ ] After contract stabilizes, migrate portal Monaco metadata and mode handling to the same API; retain portal-specific UI behavior. Proposed commits split live catalog revision from portal migration.

Acceptance: runtime/catalog changes become visible within a stated bound without restarting the editor, and one principal's metadata never pollutes another's cache. User-function availability is scoped; built-ins still take precedence.

### Issue 8 — Add optional remote LSP and a packaged generic bridge

**Files:** new `SharpMUSH.Plugins.Lsp/{LspPlugin,Options,Endpoints,SessionFactory,WebSocketTransport}.cs`, manifest/csproj; existing LSP Program/handlers/DocumentManager refactored behind a reusable host factory; new bridge entrypoint/packaging and protocol tests.

- [ ] Prototype installed OmniSharp adaptation and endpoint-local WebSocket middleware with a failing auth/handshake test first. Confirm no library/Server dependency cycle.
- [ ] Move common LSP service setup to a factory receiving analyzer/catalog and a session-scoped document manager; standalone Program retains offline defaults. Never share one DocumentManager across clients.
- [ ] Test complete framing/fragmentation/oversize/binary rejection; concurrent sessions with identical document URIs; cancellation, partial failure, shutdown/exit, disconnect and account revocation.
- [ ] Implement selected JSON message framing and bridge translations; authenticate via header to configured HTTPS origin. Bound buffers/requests/connection lifetime.
- [ ] Reuse handlers only after actual remote process tests verify range/mode/capabilities. Disable textual rename until symbol identity/refactor semantics and no-unrelated-text-change tests pass. Definitions/references may remain document-local and must be labelled accordingly.
- [ ] Package plugin dependencies and bridge for supported editor environments; test clean client connection through private Caddy fixture. Proposed commits separate server transport and bridge.

Acceptance: two authenticated clients obtain live LSP diagnostics/intelligence with isolated docs and orderly cleanup; stdio still passes its harness. Publishing full LSP support requires feature-by-feature real-host tests, not just successful initialize.

### Issue 9 — Add future attribute/file sync with explicit conflict protection

**Files:** existing `Server/Controllers/ObjectsController.cs`, shared attribute DTOs, `Client/Services/ObjectApiService.cs`, `Client/Pages/SoftcodeTabs.cs`; `Library/Stores/IAttributeStore.cs`, new conditional-write Mediator request/handler, provider implementations and service interface; later editor `sync/*` commands and tests.

- [ ] Decide transport/auth for reads/writes: current attribute API uses account-session authorization; MCP Basic analysis access must not silently grant this API or new write tools. Prefer an explicit sync capability/policy using existing attribute permissions.
- [ ] Pull first: read only visible authorized attributes, include stable world identity + full object incarnation + canonical attribute path + opaque revision. Preserve raw multiline text verbatim; local URI and local file path are never server filesystem paths.
- [ ] Add failing atomic concurrent-write, deleted/recreated object, ABA edit, missing attribute creation/tombstone, permission/ownership change and clear-versus-empty tests.
- [ ] Extend store contract with atomic compare-and-set, backed by actual transaction/provider primitives. Invoke through permission service and cache-invalidating Mediator command. Never implement get/check/ordinary SetAttribute as a race-prone substitute.
- [ ] Support expected revision/conditional request without breaking legacy PUT callers; return conflict with fresh authorized remote revision/content, not unauthorized content. Legacy overwrite remains compatibility behavior, not editor default.
- [ ] Editor records base content/revision separately from working file. Pull warns on dirty edits; push explicit only, compares base revision, presents three-way conflict and offers safe reload/merge. No auto force overwrite; any force path needs explicit permission/UI decision.
- [ ] Test saving while user keeps typing: only accepted snapshot becomes saved base, following existing SoftcodeTabs.MarkSaved intent. Preserve new edits and handle clear semantics.
- [ ] Proposed commits: `Expose revisioned attribute reads`, `Add atomic conditional attribute writes`, `Add explicit editor pull and conflict-safe push`.

Acceptance: two builders cannot lose each other's updates, recycled DBRefs never redirect a file, and permissions are checked on every operation. Synchronization never interprets whitespace or `%r` as an implicit conversion. Files do not become persistent credentials/configuration.

### Issue 10 — Document, observe and roll out compatibility

**Files:** LSP/extension READMEs, plugin docs/manifests, deployment compose/Caddy examples, appsettings defaults; redacted health/metrics; release notes.

- [ ] Document operator plugin install/disable/restart and client URL/auth/capability matrix, supported extensions, certificate failures and offline behavior.
- [ ] Add compatibility tests for old MCP schemas/config keys/default false, old local stdio entrypoint, alias semantics, absent plugin routes and legacy attribute PUT.
- [ ] Add counters for duration/cancellation/rejection/connection counts/catalog revision, avoiding code/text/credential labels and high-cardinality document identifiers.
- [ ] Test rollback to disabled plugin and prior package using disposable fixture; identify new schema/revision migrations before sync rollout. Readiness shows unavailable transport, not a false healthy endpoint.
- [ ] Prepare owner-review deployment checklist only after acceptance. Activation stays opt-in; no live deployment from this plan.

Acceptance: operator can diagnose connection problems and disable transports without harming game boot; clients know which features/source are active; prior clients have documented compatible behavior.

## 6. Verification commands and evidence discipline

Use the repository's pinned SDK and current test runner. Build affected projects sequentially; do not pass several projects to a single `dotnet build` command. Example commands from checkout root:

```bash
dotnet build SharpMUSH.LanguageServer/SharpMUSH.LanguageServer.csproj --no-restore
python3 scripts/test-lsp-stdio.py
dotnet build SharpMUSH.Tests/SharpMUSH.Tests.csproj --no-restore
dotnet SharpMUSH.Tests/bin/Debug/net11.0/SharpMUSH.Tests.dll --treenode-filter '/*/SharpMUSH.Tests.CodeAnalysis/*/*'
dotnet SharpMUSH.Tests/bin/Debug/net11.0/SharpMUSH.Tests.dll --treenode-filter '/*/SharpMUSH.Tests.LanguageServer/*/*'
git diff --check
```

Confirm the harness's invocation shape before reusing commands; do not assume a future CLI stays identical. Discover exact MCP TUnit filters from current tests instead of inventing namespace names. Newly proposed projects need restore/publish checks and actual plugin-load tests. Network restore/build escalations are governed by sandbox review; no system installation workaround.

For each phase save a brief command/SDK/base revision/result log and emitted artifact path. Avoid logging Authorization or fixture passwords. Apply repository whitespace formatting to changed source folders and verify convergence. Treat failed or cancelled runs as such; old complete integration logs do not certify new code. Repeat full disposable integration after runtime-affecting changes only when approved fixture dependencies are available.

## 7. Minimal vertical slice and stop point

The smallest useful remote slice is: reviewed offline fixes → bounded principal-aware MCP → optional published MCP plugin → private real-host/Caddy fixture → packaged VS Code MCP adapter showing diagnostics, built-in/plugin hover/completion and function signatures for all five modes/extensions. It must demonstrate live catalog provenance, no world writes, two-user isolation, reconnect and disable/rollback. Catalog revision basics are included; user-function expansion, portal unification, full remote LSP and sync are separate later slices.

This sequence intentionally gives the owner a concrete remote editor experience before committing to a generic LSP transport or write API. If generic LSP is a first-release requirement, substitute issue 8 after the fixture and use its bridge in issue 6; don't build both editor transports concurrently without that decision.

## 8. Open owner decisions before implementation scope is agreed

1. **Editor scope:** accept MCP-backed VS Code first, or require generic/full LSP in the first release? Recommendation: MCP first.
2. **Supported clients:** VS Code only initially, or named additional editors/MCP clients? Their header/OAuth support determines auth work, not marketing assumptions.
3. **Packaging:** separate shipped `mcp` and `lsp` plugins? Recommendation: yes, boot-time disabled by default, shared host analysis/identity.
4. **Authentication roadmap:** keep explicit character Basic over TLS first, or require account-scoped tokens/OAuth before release? Current Basic is available; an OAuth-only client will require new host work.
5. **Catalog disclosure:** which restricted/global/owner-local user functions may appear to each principal? Recommendation: built-in/plugin metadata first, explicit permission-filtered user-functions later.
6. **Limits:** adopt proposed small defaults after benchmarks, and select session revocation/revalidation guarantees? Never accept unbounded docs/connections.
7. **Disposable fixture:** authorize ephemeral fake-character creation in the private fixture when testing begins? No credentials have been created for this proposed fixture.
8. **Sync semantics:** read-only pull first; require atomic conditional writes before pushes? Recommendation: yes. Choose revision persistence and empty/clear behavior within existing game semantics.
9. **Release destinations:** maintained extension location/platforms and eventual plugin package publication; no publication in current scope.

## 9. Remaining risks and design choices

Plugin dependencies can load separate contract copies; published-load tests are mandatory. Endpoint registration failure may leave partial mappings; explicit failure behavior is needed. Live analysis currently lacks a single principal-aware catalog revision spanning every registry. Parser cancellation and worst-case complexity require measurement, not merely deadlines. MCP formatting may alter literal whitespace; don't enable it in the editor until proven. Existing rename is textual; safe rename needs a separate semantic design. Account/character revocation must affect long-lived sockets. Proxy misconfiguration can cause redirects and wrong rate keys. Optional plugin boot failure needs clear diagnostics without enabling unauthenticated routes. Attribute conditional writes must cover every supported store and cache invalidation. Editor credentials must never follow an unapproved redirect origin.

This plan creates no new endpoints, credentials, containers or network exposure. Draft issues above have not been posted. The next action is owner review of the minimal slice and decisions; then execute one bounded issue at a time with fresh evidence.

## Implementation milestone — 2026-10-11

Owner authorized local phases 1–4, with a 5 MB buffer requirement. The first bounded slice revalidated phase 1 and added immutable generated offline catalog/capability payloads (phase 2 subset). Fresh checks and remaining work are in task-root `PHASES-MILESTONE.md`. No live catalog provider, transport capability endpoint, document quotas or plugin extraction has been implemented yet.
