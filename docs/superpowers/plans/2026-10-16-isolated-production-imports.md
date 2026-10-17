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
