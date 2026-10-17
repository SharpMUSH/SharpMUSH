# Discord channel access implementation plan

**Goal:** Implement issue #1998: reversible channel gating and explicit unrestricted Discord speech.
**Architecture:** Persist restricted-by-default mode and permission baselines on the channel link. Reconcile only posting bits owned by SharpMUSH; retain links on cleanup failure. Queue external speech with a typed persistent identity and no game-character authority.
**Authorization:** Owner requested implementation in a PR; root approved fail-closed external send-lock semantics and conservative legacy cleanup.

- [x] Add failing restricted/default/unrestricted inbound and persistence tests.
- [x] Implement permission transport seam, baseline capture, compare-and-restore, retryable unlink and mode transitions; test real adapters and failures.
- [x] Add typed external feed identities, persistence, safe rendering and queued execution revalidation; test actual feed adapter and downstream history.
- [x] Add terminal and portal mode controls and help, preserving normal character/guest feed permissions.
- [x] Run focused Discord/feed/integration/component suites and builds; fix independent review findings.
- [ ] Complete coordinator final review, incorporate current main, create PR closing #1998 and verify CI.

## Execution rulings and evidence

- External speakers have typed persistent provider/user/name metadata and no character (`#-1`). Configured kind/feed send locks refuse external input. Recipient interaction locks refuse external input; external speakers have no game reality identity. Existing game character and guest send paths retain their normal permissions.
- Lifecycle and publication serialization waits before engine queue admission. Queued character/owner/kind checks run freshly; cancellation abandons unclaimed work and retains the fence until claimed publication finishes. Queue-side speaker selection uses nonblocking acquisition.
- Permission undo uses transforms against fresh REST state, preserving unrelated and differing administrator bits. Deleted roles require no recreation to restore. Replacement roles get their own baseline before gating. Older links conservatively release only matching historical bot values to Inherit because their original baseline was not recorded.
- Provider exact ownership lookup now honors full creation identity, with bare DBRef compatibility preserved.
- RED observed for unrestricted inbound (0 speeches), actual plugin factory cleanup (missing adapter), external recall sentinel parsing, stale full-character ownership lookup, and replacement-role undo. Final regressions pass.
- Final core: 184/184; feed suites: 41/41; account/session provider: 24/24; help articles: 40/40. Core and engine builds: 0 warnings, 0 errors. Coordinator reports full bUnit 2582/2582. Formatting ran twice on affected projects; `git diff --check` clean.
- Real Discord guild/network smoke was not run; transport tests drive the actual adapter against controlled REST snapshots, including delayed gateway state and role deletion.
