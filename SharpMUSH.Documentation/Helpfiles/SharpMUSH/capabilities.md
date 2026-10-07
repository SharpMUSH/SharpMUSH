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
# Administrative capabilities

Administrative capabilities are the permission scopes that guard snapshots, recurring jobs, the queue, profiling and reality layers. They are granted through roles, in the game and on the portal alike, using the account's role assignments and per-account overrides, and they are kept in world backups. They change nothing about PennMUSH flags, powers, ownership checks or locks.

For roles from a player's side see [roles]; for the commands see [@role] and [@permission].

## Grant and deny resolution

Each scope is decided on its own, the way Discord computes permissions. The first rule that applies wins:

1. The owner, the account linked to player #1, holds everything.
2. A held role that allows `administrator` grants everything, and overrides do not apply.
3. A per-account override, Allow or Deny, decides.
4. Any held role that allows the scope grants it, whatever its priority.
5. Any held role that denies the scope refuses it.
6. The `everyone` role decides; a scope nobody allows is refused.

Priority is never consulted here; it only orders who may manage whom (see [administrative capabilities delegated role management]).

Within one role or override set, a narrower scope left on Inherit takes the setting of the scope above it, so an explicit setting on the narrower scope wins over its parent.

## Capability scopes

| Scope | Guards |
| --- | --- |
| `snapshots.capture` | capturing and listing [object snapshots] |
| `snapshots.restore` | previewing, restoring and listing object snapshots |
| `jobs.manage.own` | your own account's [recurring jobs] |
| `jobs.manage` | every account's recurring jobs |
| `queue.inspect.own` | inspecting your own queued work |
| `queue.inspect` | inspecting anyone's queued work |
| `queue.control.own` | controlling your own queued work |
| `queue.control` | controlling anyone's queued work |
| `diagnostics.profile` | [@profile] |
| `reality.admin` | [@reality] |

Each scope without `.own` implies its `.own` form. An `.own` scope still needs you to own the thing it acts on. Restoring never follows from capturing.

The check happens where the work is done, including after it has waited in the queue.

## Actor identity and revocation

On the portal, an action is taken by the account as a whole: every character linked to it counts.

In the game, an action is taken by the player you are playing, on behalf of the account it is linked to. A player on no account, or on a disabled one, has no capabilities. Only that player's own flags decide which tier roles (`wizard`, `royalty`) it brings; another character on the same account does not raise it.

Objects you own, other people's characters and privileged callbacks cannot borrow your account's authority.

Every check reloads the account's status, its characters, its roles and its overrides. Queued work stores who it runs as, never a copy of their grants, so a transfer, unlink, disabled account or revoked role applies when the work runs, not when it was queued.

## Delegated role management

Every role change, from `@role`, `@permission` or the portal, needs `roles.admin`, and follows Discord's role hierarchy. A manager:

- creates, edits, deletes, assigns and removes only roles below their own highest role;
- changes only accounts whose highest role is below theirs, and never their own;
- allows only scopes they hold themselves.

System roles keep their name and priority, are never deleted, and are never assigned by hand. The owner is exempt from all of this, so the game can always be recovered.

`@role/player` and the portal's effective-permission view report which rule decided each scope, and which roles.

::: seealso
- [roles]
- [@role]
- [@permission]
- [security]
:::
