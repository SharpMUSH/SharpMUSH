# Social Account Login Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Discord, Google, Microsoft and Grapevine account sign-in, explicit identity/security management and independent native character credentials while preserving every existing login method.

**Architecture:** Keep custom accounts and AccountSession authoritative. Add immutable external identities in Lightning, framework remote handlers, browser-bound one-use completion and recent proof. Separate native one-time secrets from browser OTT and serialize credential/ownership decisions through a shared credential gate.

**Tech Stack:** .NET 11 pinned SDK, ASP.NET Core OIDC/OAuth handlers, Lightning/LMDB, Mediator, Blazor, TUnit/bUnit.

**Spec:** `docs/superpowers/specs/2026-10-16-social-login-design.md`, approved by owner on 2026-10-16 with Grapevine added and explicit preservation of old login methods.

## Global Constraints

- Existing account-password, character-password connect, account login, browser OTT, remembered login, passkeys and reconnect behavior must keep working.
- Migrated accounts retain character-password account fallback; never automatically disable it. New social accounts default false. An explicit proved owner action may change it.
- Four disabled-by-default providers: Discord, Google, Microsoft, Grapevine. Stable issuer/subject identity; never email matching or bot-link reuse.
- AccountSession remains the only portal credential. Never persist remote access tokens or put credentials in navigation URLs.
- Recent proof is five minutes; browser handoff 60 seconds; signup five minutes; native secret two minutes, 256 random bits, `nc1_` prefix.
- Native limits: 5 mints/minute/account, 10/minute/IP, 10 outstanding/account; 10 failed redemptions/minute/IP; bounded memory and cleanup.
- All game-state requests carry cache policy through Mediator; handlers use aggregate stores, never provider/cache objects.
- Disk scratch `/temp/codex/social-account-login`; set TMPDIR/TMP/TEMP. Never production deployment.
- Grapevine is OAuth authorization-code + `profile`, `/users/me` uid under `https://grapevine.haus`; do not assume OIDC or unsupported PKCE.

## Review Focus

- Existing accounts with only linked-character credentials must still enter portal/account-menu after migration; dedicated proof must reject fallback.
- Parallel identity signup/link/unlink must not duplicate owners or remove the final usable method.
- Provider configuration change, wrong browser, replay or revoked source session must invalidate pending completion.
- Recycled character key or ownership change must invalidate pending native authority before final bind.
- Untrusted remote errors and parser failures must never log raw codes, tokens, connect commands or secrets.

---

### Task 1: Durable account and identity foundations

**Files:** Modify `SharpMUSH.Library/Models/SharpAccount.cs`, `Stores/IAccountStore.cs`, `Services/AccountService.cs`, `SharpMUSH.Database.Lightning/Records/AccountRecord.cs`, `LightningDatabase.Accounts.cs`, `Migration/LightningMigration.cs`, `Store/Tables.cs`; create `Models/ExternalIdentity.cs`, `Stores/IExternalIdentityStore.cs`, `LightningDatabase.ExternalIdentities.cs` and corresponding Mediator requests/handlers. Tests: account service and Lightning account/identity tests.

**Interfaces:** Produce `ExternalIdentityKey(string Registration, string Issuer, string Subject)` and `ExternalIdentity` (id/account/key/label/timestamps); `IExternalIdentityStore` with lookup/list, atomic provision/attach, last-use and guarded removal. Add nullable account hash and `AllowCharacterPasswordAccountLogin`. Preserve existing store signatures where compatible and provide explicit social-account provisioning transaction.

- [ ] Write regression tests for old account hash, linked-character fallback, named-character account login, direct connect, bootstrap empty hash, and idempotent migration preserving links/sessions.
- [ ] Run focused tests and capture failing new fallback/social expectations.
- [ ] Implement migration plus optional-password representation; enable fallback for existing records and preserve existing registration behavior; social provisioning explicitly disables it.
- [ ] Add identity transaction tests for full canonical tuple, collisions, same-email nonmerge, concurrent signup/link, deleted account refusal and same-provider multiple identities.
- [ ] Implement canonical length-prefixed SHA256 key and full tuple equality, atomic provision/attach/removal with account-method count checks.
- [ ] Run focused tests, format twice, commit foundations.

### Task 2: Credential proof, revocation and recovery boundary

**Files:** Create `Services/AccountCredentialGate.cs`, `Services/AccountSecurityService.cs`, `Services/Interfaces/IRecentAccountProofStore.cs`, `Services/RecentAccountProofStore.cs`; modify session-store interfaces/implementations, account controller/service, passkey service and admin accounts controller. Tests: proof store, security, session, recovery.

**Interfaces:** Gate exposes async account-scoped execution; proof store issues/takes operation-bound grants using session digest and account id, five-minute fixed expiry. Session store adds non-sliding live validation. Security service verifies dedicated account password or matching passkey/external proof, coordinates credential mutations/revocation and explicit legacy upgrade.

- [ ] Add failing tests for expired/replayed/wrong-session proof; fallback/native strings rejected by dedicated proof; remembered login is not proof; non-sliding check leaves expiry unchanged.
- [ ] Implement bounded proof store, credential epochs/gate, session revocation hooks and owned-character mutation serialization.
- [ ] Add failing tests for first/change/remove password, passkey/email proof, final-method race, forced change, explicit legacy-only upgrade preserving current login.
- [ ] Implement security endpoints retaining existing request forms, optional proof path and replacement-session response after account-wide revocation.
- [ ] Add staff recovery tests for self/equal/higher/inactive privileged/owner/reserved targets and recent actor proof; implement persisted-grant hierarchy guard, audit and full revocation without enabling disabled targets.
- [ ] Run security/session/passkey/recovery tests, format twice, commit.

### Task 3: Four provider configurations and remote protocol integration

**Files:** Create `SharpMUSH.Server/Authentication/External/` provider definitions/configuration/adapter and framework registration; modify startup/secret definitions and actual built-in admin configuration-tile contract; create deterministic handler/backchannel tests.

**Interfaces:** `ExternalProviderRegistration` contains stable registration, provider, trusted HTTPS public origin, client id, Microsoft audience/tenants, enabled/config generation. `ExternalProviderCatalog` exposes ready public summaries and validated effective configuration, never secret values. Adapter returns `ExternalIdentityKey` plus bounded display label after validated handler proof.

- [ ] Add failing config tests: all disabled, invalid production origin/userinfo/remote URL refused, secret absent/error, immediate disable and generation invalidation, restart-required effective status.
- [ ] Add only framework-aligned OIDC dependency needed; register named remote schemes without changing default AccountSession scheme.
- [ ] Configure Google/Microsoft OIDC code+PKCE/nonce validation, Microsoft-aware concrete tenant issuer validation and configured audience; Discord and Grapevine OAuth code exchange/userinfo using official capabilities and minimal scopes.
- [ ] Add deterministic refusal/success tests for denied/missing/wrong claims, issuer/audience/tenant, token exchange errors and no token persistence; read current official protocol references when implementing adapters.
- [ ] Add four distinct Sign-in provider admin tiles with real server readers/validation, secret links/status and affected-account disable acknowledgement; integrate #1984 contract after available.
- [ ] Run provider/config/auth-scheme tests, format twice, commit.

### Task 4: Browser-bound login, signup and identity management journey

**Files:** Extract shared login completion from `Controllers/AuthController.cs`; create `ExternalAuthController.cs`, `AccountSecurityController.cs`, transient external-flow store; modify `AccountAuthService.cs`, `Login.razor`, account security components; add `Pages/ExternalLoginComplete.razor`.

**Interfaces:** Start endpoints create purpose/return/remember/account/session/config-bound transaction. Remote completion issues only browser-bound opaque handoff; POST handoff returns existing `AccountLoginResponse` or pending signup; signup consumes five-minute transaction only after successful atomic provision.

- [ ] Write failing flow tests for same-origin/CSRF, wrong browser/account/session, open redirect, handoff/callback replay, config disable/change and username collision retry.
- [ ] Extract shared session completion preserving bound character, permissions, remember policy, bans/logins/sitelock and MustChangePassword.
- [ ] Implement start/callback/handoff/signup/link/reauth flows, clearing transient cookies at terminal outcomes and never authenticating APIs via external cookies.
- [ ] Implement provider buttons, completion/signup page and explicit account-wide identity list/link/unlink/security proof UI, supporting multiple identities per provider and optional account password.
- [ ] Add bUnit tests for password/passkey methods still visible, provider readiness, passwordless first password and security-removal signout warning.
- [ ] Run flow/auth/bUnit tests, format twice, commit.

### Task 5: Independent character passwords and native secrets

**Files:** Create `INativeLoginSecretStore.cs`, `NativeLoginSecretStore.cs`, native credential service; modify account character endpoints, `SocketCommands.cs`, ownership/session revocation hooks and Characters UI. Tests: native store/service/connect/resume and character controller/UI.

**Interfaces:** Mint returns plaintext once, opaque id, full DBRef and expiry; status excludes plaintext. Store contains only digest/session digest/full character identity/epochs under shared credential gate. `connect token nc1_...` dispatches separately and reuses normal player login completion.

- [ ] Add failing atomic many-consumer, expiry boundary, capacity/rates, purpose separation, redaction and restart invalidation tests.
- [ ] Implement bounded digest-only store with cleanup, non-sliding source-session revalidation and explicit revocation.
- [ ] Add owned-character tests for nullable creation password producing nonempty unreachable random hash, failed attach reconciliation, full-identity owner password reset and recycled/transfer/ban races.
- [ ] Implement exact-owned credential writes under provider boundary/gate, mint/redeem final-bind validation and revocation hooks without changing existing persistent or browser OTT login paths.
- [ ] Implement generate/countdown/copy/revoke native panel, no-store responses and secure-transport advice; native credentials never enter account/claim/password APIs.
- [ ] Run native/connect/OTT/resume/bUnit regressions, inspect captured logs for raw secrets, format twice, commit.

### Task 6: Integration acceptance, documentation, review and CI

**Files:** Update auth ADR, deployment/provider/recovery guide, approved spec status and work evidence; update issue #1983 and PR #1992 title/body with four providers and old-method preservation.

- [ ] Run focused foundation/security/provider/flow/native suites, auth integration and affected bUnit tests; resolve failures before expanding scope.
- [ ] Run pinned-SDK server/client/test builds, relevant Node suite, formatting twice and `git diff --check`; retain exact evidence in task scratch.
- [ ] Document provider registrations/callbacks/scopes, restart/effective-state/disable impact, secret/DataProtection backup, optional-password/native journey, fallback/recovery limits, plain-telnet exposure and real-provider smoke matrix.
- [ ] Obtain independent full-branch review; fix load-bearing findings and run targeted verification.
- [ ] Push own branch, update existing PR, wait for current-head CI and address comments. Real provider credentials remain operator supplied; mark real-provider smoke unverified unless performed.

## Self-review and execution ruling

All spec sections map to Tasks 1-6; each Review Focus condition has explicit regression coverage. Store signatures finalize alongside their transactional contracts before dependent task dispatch. Existing behavior preservation has priority over proposed redesign details. Owner explicitly authorized immediate implementation and parallel subagents; no additional plan-approval pause is required. Track progress here and in the plan workspace ledger; implementation is incomplete until all six tasks and review/CI are complete.
