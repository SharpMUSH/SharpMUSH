<!-- help-article
{
  "corpus": "help",
  "id": "role-command",
  "lookup": "@role",
  "aliases": ["@roles"],
  "sections": [
    {
      "id": "role-viewing",
      "heading": "Viewing roles",
      "lookup": "@role viewing"
    },
    {
      "id": "role-editing",
      "heading": "Creating and editing roles",
      "lookup": "@role editing"
    },
    {
      "id": "role-assigning",
      "heading": "Assigning roles",
      "lookup": "@role assigning"
    },
    {
      "id": "role-overrides",
      "heading": "Per-player overrides",
      "lookup": "@role overrides"
    },
    {
      "id": "role-rank",
      "heading": "Who may change what",
      "lookup": "@role rank"
    }
  ],
  "redirects": {
    "@role2": "@role editing",
    "@role3": "@role overrides"
  }
}
-->
# @role

`@role[/list]`<br>
`@role[/info] <role>`<br>
`@role/player [<player>]`<br>
`@role/scopes`<br>
`@role/create <role>[=<display name>]`<br>
`@role/delete <role>`<br>
`@role/rename <role>=<display name>`<br>
`@role/color <role>=<#rrggbb|none>`<br>
`@role/priority <role>=<number>`<br>
`@role/allow <role>=<permission> [<permission> ...]`<br>
`@role/deny <role>=<permission> [<permission> ...]`<br>
`@role/clear <role>=<permission> [<permission> ...]`<br>
`@role/assign <player>=<role>`<br>
`@role/unassign <player>=<role>`<br>
`@role/allow *<player>=<permission> [<permission> ...]` (and `/deny`, `/clear`)

@role shows and manages roles: named sets of permissions that decide what a player may do in the game's administrative commands and in the web portal. See [roles] for how roles combine.

A role is named by its short name (`moderator`), which is lowercase letters, digits, `-` and `_`. Its display name, colour and priority can change; its short name cannot.

## Viewing roles

`@role` and `@role/list` list every role, highest priority first. `@role <role>` shows one role: its priority, colour, and what it allows and denies. `@role/scopes` lists every permission, with the narrower permissions each umbrella permission covers.

`@role/player` shows your own roles, any overrides on your account, and the permissions you hold and lack. `@role/player <player>` shows another player's, and needs the `players.view` or `roles.admin` permission.

Anyone may list roles and look at one.

## Creating and editing roles

`@role/create <role>` makes a new role at priority 1 that allows nothing; give it a display name with `=<display name>`. Then use `@role/allow` to say what it grants and `@role/priority` to place it.

`@role/allow`, `@role/deny` and `@role/clear` set each listed permission on the role to Allow, Deny or neither. `@role/rename`, `@role/color` and `@role/priority` change those fields; `@role/color <role>=none` removes the colour. `@role/delete` removes a role and takes it away from everyone who held it.

The system roles (`everyone` and the tiers `guest`, `player`, `builder`, `royalty`, `wizard` and `god`) cannot be created, deleted or moved, but what they allow can be edited like any other role.

Examples:
```sharp
@role/create storyteller=Storyteller
@role/priority storyteller=14
@role/allow storyteller=wiki.delete media.admin
```

## Assigning roles

Roles belong to accounts, not characters, so a role given to one character is held by every character on the same account. `@role/assign <player>=<role>` gives the role to the account `<player>` is linked to, and `@role/unassign` takes it away. A player without an account cannot hold roles.

The system roles are never assigned by hand. Every account holds `everyone`. The tier roles follow the character being played: a WIZARD holds `wizard`, `royalty`, `builder` and `player`; ROYALTY holds `royalty`, `builder` and `player`; a character with the Builder power holds `builder` and `player`; any other character holds `player`. Player #1 holds `god` and every permission.

Examples:
```sharp
@role/assign Ariel=moderator
@role/unassign Ariel=moderator
```

## Per-player overrides

Put `*` before a player's name to set a permission on that player's account instead of on a role. An override beats every role the player holds: `@role/deny *Twink=wiki.edit` takes wiki editing away even though the `player` role allows it, and `@role/allow *Ariel=wiki.delete` grants it without a role. `@role/clear *<player>=<permission>` removes the override. This is RhostMUSH's `@power` and `@depower`, and Discord's per-member permission override.

An override cannot touch `administrator`, and nothing overrides a role that allows `administrator`.

Examples:
```sharp
@role/deny *Twink=wiki.edit media.upload
@role/clear *Twink=wiki.edit
```

## Who may change what

Every change needs the `roles.admin` permission, and follows Discord's role hierarchy:

- You can create, edit, delete, assign or unassign only roles whose priority is below your own highest role.
- You can change the roles and overrides of a player only when their highest role is below yours. You cannot change your own.
- You can only allow permissions you hold yourself.

Player #1 is exempt from all of these.

**See Also:**
- [roles]
- [ROLES()]
- [HASROLE()]
- [PERMISSION()]
- [@power]
