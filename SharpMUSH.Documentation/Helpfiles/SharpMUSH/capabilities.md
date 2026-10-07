<!-- help-article
{
  "corpus": "help",
  "id": "administrative-capabilities",
  "lookup": "administrative capabilities",
  "aliases": [],
  "sections": [
    {
      "id": "grant-and-deny-resolution",
      "heading": "Grant and deny resolution",
      "lookup": "administrative capabilities grant and deny resolution"
    },
    {
      "id": "capability-scopes",
      "heading": "Capability scopes",
      "lookup": "administrative capabilities capability scopes"
    },
    {
      "id": "actor-identity-and-revocation",
      "heading": "Actor identity and revocation",
      "lookup": "administrative capabilities actor identity and revocation"
    },
    {
      "id": "delegated-role-management",
      "heading": "Delegated role management",
      "lookup": "administrative capabilities delegated role management"
    }
  ]
}
-->
# Administrative Capabilities

Roles are the shared source for delegated administrative operations, in the game and in the
portal. They use the account role assignments and per-account overrides, and persist in world
backups. Adding capabilities does not change Penn-compatible flags, powers, ownership checks or
locks. See help roles for the player-facing description and help @role for the commands.

## Grant and deny resolution

Resolution follows Discord's permission computation, one scope at a time. The owner (the account
linked to player #1) holds everything. A held role that allows administrator grants everything
and bypasses overrides. A per-account override (Allow or Deny) decides next. Otherwise any held
role that allows the scope grants it regardless of priority; failing that, a role that denies it
refuses it; failing that, the everyone role decides, and a scope nobody allows is denied.
Priority is never consulted. Within one role or override set, a child scope left on Inherit takes
the setting of its umbrella scope, so an explicit child setting wins over the umbrella.

## Capability scopes

The stable action scopes are snapshots.capture, snapshots.restore, jobs.manage.own,
jobs.manage, queue.inspect.own, queue.inspect, queue.control.own, queue.control,
diagnostics.profile and reality.admin. The administrative job/queue scopes imply their
corresponding own scopes. Own scopes still require a separate resource-owner check.
Snapshot restore never follows from capture. Feature implementations enforce these gates
where the operation executes, including after waiting in a queue.

## Actor identity and revocation

Portal HTTP actions resolve the account as a whole: every linked character's tier counts.
Game entry points call GetGameActorAsync with the actual executor full objid to resolve
the linked account; unlinked or disabled accounts have no capability actor.
Game actions supply the active player's full objid and that same player as executor, and only
that character's flags choose its tier roles. Another linked character's flags do not elevate
it. Owned objects, foreign characters and privileged callbacks cannot borrow the account's
authority. The executing service reloads account status, character links, roles and overrides on
every authorization; queue records store identities, never cached grants. Transfer, unlink,
disable and revocation therefore apply when queued work executes.

## Delegated role management

Every role change, from @role, @permission or the portal, goes through the role management service and needs
roles.admin. Following Discord's role hierarchy, a manager creates, edits, deletes, assigns and
removes only roles below their own highest role, changes only accounts whose highest role is below
theirs, never changes their own account, and allows only scopes they hold. System roles keep their
slug and priority, are never deleted and are never assigned by hand. The owner is exempt, which
keeps recovery possible whatever the roles say. The effective-permission API and @role/player
report which layer decided each scope and the deciding roles.
