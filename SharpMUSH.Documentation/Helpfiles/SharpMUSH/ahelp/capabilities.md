# Administrative capabilities

Portal roles are the shared source for delegated administrative operations. They use the
existing account assignments and persist in world backups. Adding capabilities does not
change Penn-compatible flags, powers, ownership checks or locks.

Each scope has Allow, Deny or Inherit. The highest-priority explicit opinion wins; Deny
wins a priority tie. A child scope with any resolved explicit opinion uses that opinion.
Only children without an explicit opinion inherit an allowed parent. Therefore an
explicit child denial survives even a higher-priority parent grant. A higher-priority
explicit child Allow can override a lower-priority child Deny. No grant means denied.

The stable action scopes are snapshots.capture, snapshots.restore, jobs.manage.own,
jobs.manage, queue.inspect.own, queue.inspect, queue.control.own, queue.control,
diagnostics.profile and reality.admin. The administrative job/queue scopes imply their
corresponding own scopes. Own scopes still require a separate resource-owner check.
Snapshot restore never follows from capture. Feature implementations enforce these gates
where the operation executes, including after waiting in a queue.

Authenticated account-only portal actions retain the existing account role derivation.
Game entry points call GetGameActorAsync with the actual executor full objid to resolve
the linked account; unlinked or disabled accounts have no capability actor.
Game actions supply the active player's full objid and that same player as executor.
Another linked character's flags do not elevate that active player. Owned objects,
foreign characters and privileged callbacks cannot borrow the account's authority.
The executing service reloads account status, character links and persisted roles on
every authorization; queue records store identities, never cached grants. Transfer,
unlink, disable and revocation therefore apply when queued work executes.

Delegated role managers can manage lower-priority grants they already hold. They cannot
edit assigned roles, change their own assignments, modify system roles or remove deny
restrictions. The account linked to player #1 administers those changes. The God role
must retain roles.admin to preserve recovery. Existing roles and assignments are not
rewritten on upgrade; explicitly grant newly introduced scopes on existing installations.
The effective-permission API reports the winning priority and role slugs, explicit or
implied resolution, and default denial. The portal permission matrix uses the same scope
catalog and localized descriptions.
