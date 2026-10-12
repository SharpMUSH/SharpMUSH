# Analysis transport validation

The implementation targets an LSP3.17 subset and uses MCP SDK2.2.0's supported negotiated revisions.
It does not claim conformance with every method of either protocol. See [remote-analysis.md](remote-analysis.md)
for the configuration, transport and reconnection contract.

## Verified locally

| Area | Evidence |
| --- | --- |
| Actual standalone stdio host | 42 assertions: initialization, valid/invalid diagnostics in all five extensions/modes, built-in intelligence, bounded variadic signatures, shutdown |
| Analyzer/handler/token regressions | 50 analyzer, 21 local LSP handler, 3 cooperative token tests |
| Catalog/parser | 10 catalog and 5 parser tests |
| Published private plugin payload | 47 isolated transport tests; shared contract/private dependency identity checked through actual plugin loading |
| MCP | 46 isolated tests including eight tools, per-principal documents, identity/catalog refresh, quotas and HTTP admission |
| Protocol revisions | Explicit MCP2025-06-18, 2025-11-25 and 2026-07-28 lifecycle/tool discovery; notification202 and invalid protocol-header400 for2025-11-25 |
| Desktop example | Strict TypeScript compilation, mocked VS Code launch/SecretStorage harness, all five extensions and actual VSIX packaging; npm audit0 |

Tests use synthetic identities/objects, streams and TestServer virtual transports. No fixture world,
listener, TLS key or persistent credential was created. TestServer identity injection validates the
analysis boundary; it does not exercise a deployed Basic-auth credential exchange.

## Protocol quality checklist

Applied and exercised: initialization and shutdown ordering; receive-order document snapshots;
UTF-16 positions and CRLF handling; version/generation stale-result suppression, including close during
projection; cancellation response uniqueness and completed/unknown cancellation; 100 completion/cancel
races;32 awaited overload replies; message/header/document/session/result bounds; quiet-client reader
fairness; fragmented-body deadlines; principal and URI isolation; Origin denial; disabled endpoint404;
plaintext/snippet capability handling; bridge disconnect with uncancellable input.

Shared executor admission includes metadata work and retains capacity until actual worker termination.
Disconnect releases stored document/session quotas first. Rejected synchronization closes explicitly
so clients must initialize/reopen on reconnect. Diagnostics recheck generations within outbound ordering.
MCP cancellation/lifecycle remains SDK-owned; the LSP dispatcher is independent.

Three reviewer regressions were also run against the reviewed pre-fix implementation and failed:
quiet-client handling, close-during-diagnostic-projection and uncancellable bridge input. The corrected
suite passes. Synchronization-capacity loss was reproduced before its fix. A single fresh whole-branch
review found no Critical and six Important findings, addressed in one fix pass with focused regressions.

## Deferred or outside this slice

- Real TLS/reverse-proxy, native bridge socket roundtrip, actual world credentials/revocation and VS Code UI require the separately approved disposable fixture.
- Attribute synchronization/writeback, world evaluation, remote formatting/rename/definitions/references and browser authentication tickets are omitted.
- Owner-local function names remain catalog metadata but are not suggested as global calls; dedicated localfun presentation needs a design.
- Portal Monaco's independent static metadata, literal-whitespace formatting semantics and standalone textual rename need separate work.
- Hard preemption, exact heap ceilings, cross-session resume, and editor automatic retry/reopen are separate designs.
- VSIX dependency bundling and the example's pre-existing license manifest/package reconciliation remain packaging follow-ups. Packaging emits warnings for those; the entrypoint and runtime dependencies are present.
- The world-backed integration suite was not run because its persistent world/network fixtures are outside current authorization. No full-suite or deployment-ready claim is made.

## Review follow-up

Command-lock projection and notification-deadline regressions failed before their fixes. Canonical
and alias command hover now retain captured lock metadata. Per-analysis notification deadlines
publish a generation-checked warning while keeping the session usable; later edits can retry.
Admission failure still closes explicitly. Authentication narrows nested object union cases directly,
with missing/non-player cases rejected before account/catalog resolution. A live-catalog regression
confirms compiled aliases exclude conflicting global names; silent collision skipping is not needed.
