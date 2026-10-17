# Isolated production import implementation

Implements the existing staging proposal and the supported Rhost offline bridge integration.

- [x] Put preview conversion in a separate worker process with its own complete provider, Mediator, cache, configuration, secrets, plugin snapshot, and disabled transport publication.
- [x] Snapshot and hash declared source inputs; strictly parse every companion before conversion writes. Bound process duration and diagnostic volume. Drop failed/cancelled private worlds and keys.
- [x] Persist preview reports and readiness only after provider disposal. Expose safe session discovery and reload/resume controls.
- [x] Separate preview from explicit promotion/restart confirmation and audit. Hold an engine lease; install the closed world and matching encryption keys before resolving live stores.
- [x] Retain the old world/key pair. Fail closed after filesystem errors; support replayable explicit rollback and an offline recovery command before NATS or provider startup.
- [x] Reset outgoing transport/resume bindings only after import/rollback, before hosted consumers start. Preserve ordinary restart behavior.
- [x] Make configured startup imports use the same private workflow with content receipts and crash-tail recovery.
- [x] Cover actual HTTP preview isolation, encrypted credentials through promotion, cancellation, old-world sentinels, interrupted world/key moves, offline recovery, and UI confirmation/resume.
- [x] Complete actual QDBM and MDBX bridge → worker → restart-promotion acceptance, exact non-garbage inventory, attributes, and immutable-source checks.
- [ ] Independent review, required current-head CI, ready PR, and issue closure after merge.

No import is deployed to a production game by this change. Installed plugins remain trusted code; the worker is an independent process and service graph, not an OS security sandbox. Historical unsupported native ABIs require a source-side export.

Verification: backend build 0 warnings/errors; affected import suites 142 passed with four native-profile skips, then focused recovery 18/18 passed and actual configured native profiles 11/11 passed with zero skips. The skipped cases were explicitly rerun with both real exporters. Full portal suite 2,596/2,596 passed, including 12 focused confirmation/refusal/resume cases. The missing-keyring guard has a recorded failing test before implementation; native promotion asserts the exact non-garbage inventory and its explicit translation warning.

Review corrections: the parent publishes Ready only after child disposal and source revalidation; cancellation cannot revoke accepted promotion intent. Startup recovers a durable authorized intent whose separate marker write was interrupted. Isolated capacity checks use the staged map ceiling and reserve the extra source copies; the private provider honors the configured map/sync settings. The private worker creates its own passwordless Package Manager outside imported live object IDs so bundled packages can bootstrap after promotion. The parity caller validates the intent, restarts the same world once, and checks both real HTTP readiness and the committed journal.

Review verification: backend build zero warnings/errors; import/recovery 22/22, capacity 8/8, focused UI 13/13, full portal 2,597/2,597, Python parity harness 45/45, and real QDBM/MDBX native integration 11/11 with zero skips. An actual PennMUSH baseline → isolated preview → restart → engine/socket smoke exercised 25 login/import steps: 15 matches, one known difference, nine baseline open gaps, zero new differences/errors. The full parity CI remains a required merge gate.

Second review corrections: discovery ignores directories interrupted before their first durable session record, while malformed existing records still fail closed. Failed initial writes remove their new directory when possible. Restart intent schedules response-completion shutdown before best-effort auditing; an audit provider failure cannot conceal an accepted promotion/rollback. Native CI watches every actual import entrypoint and HTTP regression file. The three new crash-directory/audit cases failed before their fixes; all import classes now pass 25/25, native profiles 11/11 without skips, and backend builds with zero warnings/errors. Prior checkpoint 860151474 full CI parity reported 641 matches, 24 known differences, 69 baseline open gaps, zero new differences/errors; native CI passed 11 actual native cases, 20 failure cases, and 51 strict-reader cases. The new checkpoint still requires fresh CI.

Independent review caught the distinction between a missing first journal and an unreadable existing journal: File.Exists hid access failures. An actual chmod-000 session-directory regression failed before correction; discovery now probes attributes and ignores only FileNotFoundException/DirectoryNotFoundException, preserving other I/O/access failures. All import classes pass 26/26; backend build remains zero warnings/errors.

Integrated current main b52ffb91f by retaining both import and wiki resource entries: exactly seven main keys added in each of 16 locale files, with valid XML and unique keys. Audit-failure logging now emits fixed text only, with no request-derived values. After integration, backend build has zero warnings/errors; all import classes pass 26/26 and the complete portal suite passes 2,611/2,611 (54 pre-existing build warnings). No production import or deployment was performed.
