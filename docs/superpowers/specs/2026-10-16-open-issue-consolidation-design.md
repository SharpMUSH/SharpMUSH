# Open issue consolidation

Inventory verified 16 October 2026 against GitHub: 13 open issues, no open PRs.
Base: `38bab40c33ce362fe147b9b1d209367a02193b55`, verified equal to remote main.

## Outcome and execution

Resolve confirmed bugs, make proposal contracts explicit, and deliver reviewable PRs covering every open issue. Keep existing softcode and optional application interfaces usable. Do not merge or deploy. Use six PRs, at most three active implementation worktrees and subagents at once. Reuse worktrees between completed lanes; keep each PR independently reviewable. The current detached checkout is the coordination workspace; leave its existing `.codex/` files alone.

| PR lane | Issues | Deliverable |
| --- | --- | --- |
| Argument contracts | #1788, #1789, #1790, #1791 | Named/numbered compatibility, anonymous filterq side inputs, presence metadata/query, local signatures |
| Queue contracts and accounting | #1792, #1793, #1741 | Actual trigger admissions, consistent independent entry snapshots/budgets, own-time accounting, measured bounded-work example |
| Shared application flow | #1794, #1795, #1796 | Typed state, shared schema transitions, optional ordinary-command terminal adapter, Jobs edit/review/file proof |
| Stored markup safety | #1818 | Preserve opaque stored bytes through copies/snapshots; reject unsafe edits; isolate corrupt records |
| Charset compatibility | #1798 | Renderer-aligned charset negotiation and fail-closed unsupported acceptance |
| Contract audit | #1797 | Complete registration inventory and reviewed/pending/excluded evidence; prioritized discrepancies and mechanical checks |

Initial parallel wave: arguments, applications, stored markup. Queue replaces arguments, branching from that PR's head with an explicit stacked base until its prerequisite merges, because both touch trigger and parser frames. Charset replaces the first other completed lane; audit replaces the next. Audit work owns a worker slot. Later rebase the queue PR onto main and verify its final diff. Implementers may investigate prerequisites while another lane owns overlapping files; they must not independently invent shared APIs.

## Current evidence and limits

- `ArgHelpers.NamedArguments` remains names-only; `NamedArgumentTests.UargsPassesNoPositionalArguments` deliberately asserts it. Local function registration rejects ARGS. No supplied-key provenance exists.
- `ListFunctions` already forwards anonymous filter/filterbool side arguments; `ListFunctions.FilterQ.cs` omits the shared helper's extra-argument parameter. Fix the remaining omission and protect the existing paths.
- Trigger still awaits attribute execution inline. `@dolist` snapshots were fixed in #1907; inspect remaining wait, semaphore and delayed admission paths rather than redoing that fix.
- Localize and uldefault still parse raw no-parse argument text with their own clocks running.
- Application bool/list seeding was partly fixed in #1913. Numbers and null still use display-string conversion. Form/view children still retain replacement documents internally and ignore failure replacements. Removed form fields remain in the payload, and unbound errors can disappear.
- MarkupString is already 2.21.0: the requested 2.19.0 bump is obsolete. No Inspect/CanRewrite protection was found. TNC remains 4.5.0.
- These are source findings, not completed runtime reproductions. Each bug requires a meaningful failing regression before the fix.

## Proposed decisions for review

These are coordinator recommendations, not previously agreed contracts.

### Argument contracts

Ordered supplied values acquire numbered aliases before named aliases are overlaid. Names remain case-insensitive and last-wins. Repeated names retain their distinct ordered positions. A name consisting entirely of ASCII decimal digits is a numeric name, normalized without leading zeroes; signs are ordinary name characters. Permit a numeric name only when it equals that pair's zero-based supplied position. Reject other numeric names through the entry point's existing argument-error surface before mutating any frame; do not evaluate or compare values to decide. This deliberately tightens permissive numeric naming to prevent ambiguous aliases. An empty supplied value occupies a slot; aliases never inflate `%+`. Includes overlay supplied named and numbered keys while preserving untouched inherited keys, including `/chain/args`; document the changed slots as a migration.

Examples: `who,Bob,count,3` binds who/0=Bob and count/1=3, count=2. `who,A,WHO,B` binds who=B, 0=A, 1=B. `0,A,who,B` is valid; `1,A,who,B` is rejected before binding or callback execution; validation does not force or compare values. Entry points retain their existing eager argument evaluation timing. `00,A` is equivalent to `0,A`. `who,` binds who/0 to empty and count=1. Including `who,Bob` over inherited 0=Alice,1=kept replaces 0/who with Bob and retains 1=kept.

Propose `hasarg(name[,scope])`, default scope `visible`, alternative `supplied`, invalid scope returning the normal function argument error. Outside an attribute invocation, return 0. Keep immutable supplied-key metadata with each attribute frame, captured before include merging. Generated metadata is visible but not supplied. Built-in frames preserve the surrounding attribute metadata; nested attribute calls replace it; queued work snapshots it. Querying presence never forces the target value.

Reuse the global ARGS signature grammar/validator for owner-local functions, supporting only forms already supported globally. Definition lifetime, ownership checks, namespace resolution and unsigned behavior stay intact. Match local/global binding without making their authority equivalent.

### Queue contracts

Ordinary trigger admits exactly one independent action entry; include remains inline. Preserve argument evaluation timing and identity, snapshot registers/output/logical state, and give independent entries fresh per-entry counters while retaining admission/owner/global abuse limits. Inventory every producer and prove preservation of needed iteration/capture context without unbounded obsolete-frame retention.

Use `InvocationClock.PauseRunning()` around raw argument parsing to correct attribution without changing scope. Measure real two-actor execution and queue depth for ordinary dolist, inline looping and bounded production. Prefer an existing core-supported paging pattern; a new admission API requires its own concrete design if evidence shows one is necessary. Never claim completion while admitted children remain unfinished or refused.

### Applications

Choose one shared document/state host and action-result reducer, used by dynamic applications and widgets. Normalize typed values without conflating missing, empty, null, false or zero; defaults apply only when absent. Retain removed values as local draft but submit only active editable fields. Authoritative server validation must continue to distrust all posted fields.

For a current, valid response: on success only, reset draft keys first and then merge returned fields; on either success or failure, apply replacement schema and data next, with returned data authoritative for keys it supplies; finally bind errors against the resulting presentation. Defaults seed only missing keys. Render from the resulting schema kind. Failed responses may replace presentation and bind errors but do not run success-only merge, reset, navigation or toast behavior. On success, an allowed redirect wins over rendering the updated local presentation and retires the old form generation; existing redirect validation remains mandatory. Show errors lacking visible targets in an accessible summary. Fence responses by application/path/character/form instance so retired requests cannot replace current state. Pin this precedence in reducer tests before implementing it.

Reset removes the named draft key, allowing the resulting schema's default to seed it if replacement data/fields did not supply it. Missing defaults remain missing. Retained keys suppress defaults. Serialize actions per host, disable field editing while its action is pending, and accept only the active request token in the current host generation. Cancellation/navigation retires that token even when the transport cannot cancel. No latest-response ordering guess is needed.

Choose #1795 approach A: ordinary commands for nonmodal field edits/review/back/file/cancel, with optional short existing modal prompts. Preserve current @input routing and cancellation; add no passthrough. Start with an optional softcode adapter and the same small Jobs flow on both clients; add only core seams the measured proof needs. Share declarations and authoritative validation, recheck filing permission/world state, bind drafts and retry identity to their owner/instance, and state restart limits honestly. No new general record store or workflow DSL.

The proof adds `+request/draft new`, `+request/draft set <field>=<literal text>`, `+request/draft review`, `+request/draft back`, `+request/draft file`, and `+request/draft cancel`, retaining legacy +request. Fields are bucket, title (required/max 80), text, and formatting preference. Edit→review validates without effects; Back returns to edit; File revalidates then calls the existing filing routine; Cancel discards the draft. Store transient drafts through an optional adapter seam keyed by trusted actor plus transport/form instance, expire after 30 idle minutes, and retire on logout/disconnect/character switch. Web tabs and terminal connections have distinct instances. Retry receipts bind actor, instance, operation ID and payload digest; replay returns the prior result, conflicting reuse errors. Serialize File in that instance so concurrent retries cannot repeat the effect. Document that in-memory receipts do not guarantee exactly-once across process failure; no durable workflow promise. If the existing softcode runtime cannot safely provide this ownership/serialization, isolate the smallest core transient-state seam in the plan before coding it.

### Storage and protocol

Copy original markup bytes for copy/move/snapshot/restore whenever reserialization is unsafe; reject read-modify-write edits atomically with one message. Whole replacements remain deliberate overwrites. Catch malformed rows individually. Protect scene edit/history and feed accounting. Document readers-before-writers deployment and rollback policy without inventing a world-version stamp for a nonexistent new wire format.

The attribute aggregate owns serialized-value/provenance access via Mediator requests and IAttributeStore; AttributeService retains permission checks. Model an inspected stored value containing original serialized bytes, decoded display value, inspection status and CanRewrite. Complete/Opaque follow the library's CanRewrite decision, Unsupported/Damaged remain displayable where possible but cannot be rewritten; malformed non-JSON rows produce per-record diagnostics and are skipped without aborting enumeration. Copies preserve bytes; edits preflight every matched attribute before writing any, including wildcard edits. Snapshot restore validates JSON-object structure and inspection status without relying on Deserialize throwing. Structurally valid Damaged values with a display value (such as a throwing codec) are copied raw with diagnostics; malformed/non-object/trailing-content payloads are rejected. A snapshot copy preserves source bytes rather than repairing them. Scene storage keeps equivalent original-content provenance. Pure edit() on supplied text remains a string operation; protect persisted attribute read-modify-write entry points rather than pretending evaluated strings retain source provenance.

Advertise UTF-8, Latin-1 and ASCII, in that preference order, and ensure unsupported/unoffered acceptance cannot leave the connection running with mismatched decoding/output. Prefer supported dependency configuration and validation. If TNC needs a release, prepare its branch separately and report the dependency; do not open a third-party PR without specific permission. GMCP retains fixed UTF-8.

### Audit

#1797 explicitly starts with a read-only inventory. Enumerate registered functions, commands, switches, aliases and callback contracts; trace actual editor/help consumers; record evidence/status for every entry. Treat broad behavioral changes as owned by the other lanes or separately prioritized work. Fix confirmed small metadata/help defects with executable examples once priorities are reviewed. Do not equate parameter-name count with MaxArgs or generate documentation from unverified metadata.

The audit PR delivers complete enumeration and evidence/status for every entry, prioritized discrepancies, consumer tracing, and reliable mechanical drift checks. It does not claim every entry behaviorally exercised. Prioritize the confirmed json_map/json_mod signature/help discrepancies for focused correction in this PR after reproduction; other behavior changes belong to their existing implementation lanes or explicit follow-ups. Pending audit entries remain visible and #1797 stays open until its full acceptance criteria are met.

## Validation and acceptance

Each implementation PR includes focused reproductions and tests for every issue acceptance item, a migration/compatibility note where behavior changes, and explicit unrun checks. Exercise real scheduler boundaries, identity and budgets for engine changes; bUnit renderer/action/state tests plus an end-to-end Jobs flow for applications; integration corrupt-record/copy/snapshot/history/startup coverage for storage; actual fragmented CHARSET frames, aliases and renderer bytes for protocol. Run formatting twice to convergence, dependent suites, independent review, then CI and resolve actionable review findings. Never claim an issue complete solely from source inspection or green unrelated CI.

Use the repository-pinned SDK. Keep logs and SDK scratch in workspace `work/issue-consolidation/`; runtime tests use short disk-backed `/temp/codex/<lane>/` directories, created through approved escalation, because longer workspace TMPDIR paths exceed Linux Unix-socket limits. Set TMPDIR/TMP/TEMP for tools creating temporary files. Remove task scratch when finished, keeping final evidence in the reviewable PR documentation.
