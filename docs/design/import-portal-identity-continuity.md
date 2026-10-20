# Portal identity continuity during database import

Repair scope for [#2078](https://github.com/SharpMUSH/SharpMUSH/issues/2078),
discovered during onboarding validation. This is a proposed implementation
contract, not a claim that account transfer currently exists.

## Observed failure

The isolated worker migrates a fresh database, imports MUSH objects, and carries
configuration and secrets. It does not carry portal accounts. Promotion swaps
that database into place. Bootstrap then creates a new unclaimed administrator,
but importing player #1's password has already marked setup completed.
The previous administrator cannot sign in or resume setup.

## Transfer boundary

Transfer current portal identity state after the old server has stopped and
before `ImportPromotionJournal` moves either world into place. Do not put account
credentials in the preview request, preview report, or status response.

Preview-time snapshots are insufficient: a password, passkey, external identity,
ban, or account status can change while a preview is awaiting promotion.
Promotion must preserve those later changes. A failed transfer must leave the
outgoing world usable and the staged world unpromoted.

Use an explicit provider migration operation over closed worlds. Keep provider
record formats inside the provider; the journal coordinates the operation and
durable commit state. This is not a normal request-handler write to a live store.

## Identity and authority rules

- Preserve account IDs, unique usernames/emails, password hashes, account status,
  verification state, password-change requirements, and credential policy.
- Preserve passkey credential IDs, public keys, counters, backup state and account
  ownership. Preserve external registration/issuer/subject identity tuples and
  their owning accounts. Rebuild or validate every associated index.
- Preserve account bans and authorization records with their referenced role
  definitions. Never silently reactivate a disabled account.
- Do not copy old character ownership links by matching numeric dbrefs: imported
  objects are different identities. Transfer the existing #1 owner's link only
  to the validated imported player #1, as promised by first-run setup. Other
  accounts require explicit character claiming or assignment.
- Do not copy active sessions, OTTs, pending proof grants, or character-scoped
  OAuth authority into the replacement world. Require fresh authentication.
- Preserve the claimed-setup flag and wizard-pending state independently.
  Do not overwrite the imported character's password with a portal password.
- Ensure newly created accounts cannot reuse a transferred account ID.

## Rollback

Rollback must not resurrect credentials removed or changed after promotion.
Apply the same current-identity transfer rule before swapping the previous
world back, while restoring character links only through an explicit validated
mapping. A raw reversal of directory names is insufficient for this contract.
Define and test retry behavior at each durable transition.

Legacy sessions that already swapped worlds without a prepared identity mapping
must refuse automatic rollback and leave both directories intact. They require
operator recovery with an explicit ownership mapping: guessing could overwrite
retained accounts or restore revoked credentials. Missing current-identity source
data must likewise fail closed. Pre-swap failures leave the original identity
and ownership state untouched.

When an imported object already holds a role with the same slug as a transferred
account role, conflicting permissions must stop promotion. Do not silently change
the imported object's authority. Preserve custom permission definitions referenced
by transferred accounts and roles.

## Required evidence

1. Real isolated-worker promotion with a claimed portal administrator:
   same portal sign-in works, imported #1 password remains unchanged, and the
   imported name/genre/subgenre appear in the setup identity form.
2. An unfinished wizard resumes after promotion and browser reload; deferring
   identity changes preserves imported settings.
3. Passkey-only and external-only accounts remain usable; disabled accounts and
   bans remain disabled. Duplicate indices or unresolved role references fail
   before promotion.
4. A credential changed after preview is the credential accepted after promotion;
   removed factors and stale sessions are rejected.
5. Old character links and character-scoped grants cannot authorize access to
   unrelated imported objects with the same numeric dbref.
6. Rollback preserves the latest credential state and validates its character
   mapping. Interrupted transfer/commit can recover without partial identity data.
7. Preview leaves the live database and source files unchanged, and reports never
   expose credential material.

The existing worker tests cover world isolation and configuration/secret
promotion, but do not prove these account-continuity requirements.
