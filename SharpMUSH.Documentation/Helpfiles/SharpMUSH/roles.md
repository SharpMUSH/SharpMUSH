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
    },
    {
      "id": "roles-wizard-split",
      "heading": "What a wizard's work is split into",
      "lookup": "roles wizard"
    },
    {
      "id": "roles-packages",
      "heading": "Roles and permissions from packages",
      "lookup": "roles packages"
    }
  ],
  "redirects": {
    "roles2": "roles combine",
    "roles3": "roles defaults"
  }
}
-->
# roles

A role is a named set of permissions. Roles decide what an object may do: in the game (being a wizard, the PennMUSH powers, controlling other objects) and in the web portal (wiki, media, packages, configuration, and so on). They work like the roles on a Discord server.

Both objects and accounts hold roles. An object holds the roles assigned to it; a character linked to an account also holds the account's. Nothing is inherited from an object's owner.

Each role sets every permission to one of three states: Allow, Deny, or neither (it leaves the permission to other roles). Each role also has a priority, which places it in the role hierarchy. See [@role] for the commands, and [@permission] for the list of permissions. A game can add permissions of its own, such as `bbs.moderate`, with `@permission/define` (see [@permission define]), and a package can bring its own (see [roles packages]).

## How roles combine

For each permission, the first of these that applies decides:

1. Player #1 holds every permission.
2. If any role held allows `administrator`, every permission is held, and overrides do not apply.
3. If the object has an override for the permission (`@permission/allow`, `@power`), the override decides.
4. If its account has an override for it (`@permission/allow/account`), that override decides.
5. If any role held allows it, it is held. A Deny on one role never cancels an Allow on another, whatever their priorities.
6. If a role held denies it, it is not held, even when the `everyone` role allows it.
7. Otherwise, the `everyone` role decides. A permission nobody allows is denied.

Priority does not decide any of this, and it does not decide control. It only orders roles for management: you can manage only roles, objects and accounts below your own highest role.

Some permissions are umbrellas over narrower ones: `wiki.admin` covers `wiki.read`, `wiki.drafts`, `wiki.create`, `wiki.edit` and `wiki.delete`; `media.admin` covers `media.upload`; `players.moderate` covers `players.view`; and `jobs.manage`, `queue.inspect` and `queue.control` cover their `.own` versions. A role or override that sets the umbrella but leaves the narrower permission alone applies the umbrella's setting to it. A setting on the narrower permission itself always wins within the same role.

To take a permission away from one object, use an override (`@permission/deny`). To take it away from a group, leave it off the roles they hold rather than adding a Deny role on top, because any other role's Allow wins.

## Roles a new game starts with

| Priority | Role | Allows |
|---|---|---|
| 40 | god | administrator |
| 30 | wizard | every portal permission except `server.admin` and `administrator`, plus `game.wizard`, `chat.admin`, `server.operate`, `control.all`, `protect.wizard`, `protect.admin` |
| 25 | moderator | `players.moderate`, `wiki.admin`, `media.admin`, `queue.inspect`, `queue.control`, `roles.admin` |
| 20 | royalty | `players.view`, `wiki.admin`, `media.admin`, `queue.inspect`, `game.royalty`, `protect.admin` |
| 15 | builder | `diagnostics.profile`, `game.builder` |
| 12 | helper | `players.view`, `queue.inspect` |
| 10 | player | `wiki.create`, `wiki.edit`, `media.upload`, `softcode.use`, `snapshots.capture`, `snapshots.restore`, `jobs.manage.own`, `queue.inspect.own`, `queue.control.own` |
| 0 | guest | `game.guest` |
| 0 | everyone | `wiki.read` |

`helper` and `moderator` are ordinary roles: assign them with `@role/assign`, and edit or delete them as you like. They are created only in a new game.

Permissions that act on objects still check ownership and locks. For example, `snapshots.restore` lets a player restore only objects they control.

## Roles, flags and powers

The privilege flags and the PennMUSH powers are roles and permissions:

- The WIZARD flag is the `wizard` role, and ROYALTY the `royalty` role. `@set` assigns and removes them; `hasflag()`, `flags()` and `FLAG^` locks answer from them.
- Each power is a `game.` permission: See_All is `game.see_all`. `@power` sets an Allow override on the object, and `haspower()`, `powers()` and `POWER^` locks answer from what the object holds, so a role that allows `game.see_all` gives every holder See_All. The Builder and Guest powers are the `builder` and `guest` roles.
- Control reads permissions too. `control.all` controls everything except #1 and holders of `protect.wizard`; a holder of `protect.admin` is controlled only by another holder of it. Ownership, zones and locks still decide control as in PennMUSH.

## What a wizard's work is split into

Being a wizard (`game.wizard`) is split into groups, so a custom role can hand out one part of a wizard's work without the rest. The `wizard` role allows all of them.

| Permission | Covers |
|---|---|
| `players.moderate` | @newpassword (not on a wizard), @sitelock, guests, @quota and @allquota, `checkpass()`, `connlog()`, `connrecord()` |
| `config.admin` | @config/set, @enable, @disable, @command, @function, @attribute and @power definitions, @hook |
| `packages.admin` | @package |
| `chat.admin` | Wizard channels (and Admin channels), channel privileges, @channel changes and nuking on channels you don't own, @wizwall, @wall, @rwall, the MOTDs and `wizmotd()`, @mail admin and stats, mail alias admin |
| `wiki.admin` | wiki requirements (setting them, and skipping them), protecting and publishing pages; see [wiki permissions] |
| `server.operate` | @shutdown, @dump, @dbck, @purge, @readcache, @backup, @storage, @log, @slave, @kick, @uptime details |

Everything else a wizard does still needs `game.wizard`: wizard attributes, overrides in `examine` and `@destroy`, the preserve switches, @chownall and @chzoneall, and every check on whether the target is a wizard. A check that also takes a power, such as @halt with Halt or @sql with SQL_OK, takes that power as before. Any single command can be given its own permission with `@command/restrict <command>=PERM^<permission>`, and a function with `@function/restrict <function>=<permission>`.

A power made with `@power/add` is stored on the object as before.

A PennMUSH database imported into SharpMUSH keeps every privilege: WIZARD and ROYALTY become role assignments and each power an override, on the same objects.

## Roles and permissions from packages

A package can bring the custom permissions its softcode checks, roles that allow or deny them, and the role and permission categories they go in. Installing it creates what the game lacks and uses what the game already has without changing it. A package's role sets only custom permissions, and sits below the wizard role, so installing a package never hands out a built-in permission: allow those on its role with `@role` if you want them.

An upgrade keeps your edits. A field you changed on the package's role or permission keeps your value unless the new version changes that same field. Allowing or denying something else on its role is never undone.

Uninstalling, or a version that drops an item, removes it unless the game still relies on it: a role someone holds, a permission a role still sets, or a category with something in it is kept and becomes the game's own. Removing a permission clears every override of it.

::: seealso
- [@role]
- [@permission]
- [ROLES()]
- [HASROLE()]
- [PERMISSION()]
- [lock keys]
:::
