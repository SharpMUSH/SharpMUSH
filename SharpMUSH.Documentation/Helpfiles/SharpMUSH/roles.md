<!-- help-article
{
  "corpus": "help",
  "id": "roles",
  "lookup": "roles",
  "aliases": ["role"],
  "sections": [
    {
      "id": "roles-resolution",
      "heading": "How roles combine",
      "lookup": "roles combine"
    },
    {
      "id": "roles-defaults",
      "heading": "Roles a new game starts with",
      "lookup": "roles defaults"
    },
    {
      "id": "roles-flags",
      "heading": "Roles, flags and powers",
      "lookup": "roles flags"
    }
  ],
  "redirects": {
    "roles2": "roles combine",
    "roles3": "roles defaults"
  }
}
-->
# roles

A role is a named set of permissions, held by accounts. Roles decide who may use SharpMUSH's administrative features (snapshots, recurring jobs, queue control, profiling, reality layers) and what each account may do in the web portal (wiki, media, packages, configuration, and so on). They work like the roles on a Discord server, and borrow the per-player override from RhostMUSH's `@depower`.

Each role sets every permission to one of three states: Allow, Deny, or neither (it leaves the permission to other roles). Each role also has a priority, which places it in the role hierarchy. See [@role] for the commands, and `@role/scopes` for the list of permissions.

## How roles combine

For each permission, the first of these that applies decides:

1. Player #1 holds every permission.
2. If any role you hold allows `administrator`, you hold every permission, and overrides do not apply.
3. If your account has an override for the permission (`@role/allow *you=...` or `@role/deny *you=...`), the override decides.
4. If any role you hold allows it, you hold it. A Deny on one role never cancels an Allow on another, whatever their priorities.
5. If a role you hold denies it, you lack it, even when the `everyone` role allows it.
6. Otherwise, the `everyone` role decides. A permission nobody allows is denied.

Priority does not decide any of this. It only orders roles for management: you can manage only roles, and players, below your own highest role.

Some permissions are umbrellas over narrower ones: `wiki.admin` covers `wiki.read`, `wiki.create`, `wiki.edit` and `wiki.delete`; `media.admin` covers `media.upload`; `players.moderate` covers `players.view`; and `jobs.manage`, `queue.inspect` and `queue.control` cover their `.own` versions. A role or override that sets the umbrella but leaves the narrower permission alone applies the umbrella's setting to it. A setting on the narrower permission itself always wins within the same role.

To take a permission away from one player, use an override (`@role/deny *player=...`). To take it away from a group, leave it off the roles they hold rather than adding a Deny role on top, because any other role's Allow wins.

## Roles a new game starts with

| Priority | Role | Allows |
|---|---|---|
| 40 | god | administrator |
| 30 | wizard | every permission except `server.admin` and `administrator` |
| 25 | moderator | `players.moderate`, `wiki.admin`, `media.admin`, `queue.inspect`, `queue.control`, `roles.admin` |
| 20 | royalty | `players.moderate`, `wiki.admin`, `media.admin`, `queue.inspect` |
| 15 | builder | `diagnostics.profile` |
| 12 | helper | `players.view`, `queue.inspect` |
| 10 | player | `wiki.create`, `wiki.edit`, `media.upload`, `softcode.use`, `snapshots.capture`, `snapshots.restore`, `jobs.manage.own`, `queue.inspect.own`, `queue.control.own` |
| 0 | guest | nothing |
| 0 | everyone | `wiki.read` |

Tier roles stack, so a wizard also has everything royalty, builder and player allow. `helper` and `moderator` are ordinary roles, modelled on RhostMUSH's Guildmaster and Councilor ranks: assign them with `@role/assign`, and edit or delete them as you like. They are created only in a new game.

Permissions that act on objects still check ownership and locks. For example, `snapshots.restore` lets a player restore only objects they control.

## Roles, flags and powers

Roles sit beside PennMUSH's flags and powers and do not change them: a WIZARD's in-game powers come from the flag as always. The flags only choose which tier roles a character holds (see [@role assigning]).

Roles belong to accounts. A character that is not linked to an account holds no roles, so the administrative features above refuse it.

**See Also:**
- [@role]
- [ROLES()]
- [HASROLE()]
- [PERMISSION()]
