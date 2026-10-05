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
      "heading": "Overrides",
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
`@role/player [<object>]`<br>
`@role/scopes`<br>
`@role/create <role>[=<display name>]`<br>
`@role/delete <role>`<br>
`@role/rename <role>=<display name>`<br>
`@role/color <role>=<#rrggbb|none>`<br>
`@role/priority <role>=<number>`<br>
`@role/allow <role>=<permission> [<permission> ...]`<br>
`@role/deny <role>=<permission> [<permission> ...]`<br>
`@role/clear <role>=<permission> [<permission> ...]`<br>
`@role/assign[/account] <object>=<role>`<br>
`@role/unassign[/account] <object>=<role>`<br>
`@role/allow/object <object>=<permission> [<permission> ...]` (and `/deny`, `/clear`)<br>
`@role/allow/account <player>=<permission> [<permission> ...]` (and `/deny`, `/clear`)

@role shows and manages roles: named sets of permissions that decide what an object may do, in the game and in the web portal. See [roles] for how roles combine.

A role is named by its short name (`moderator`), which is lowercase letters, digits, `-` and `_`. Its display name, colour and priority can change; its short name cannot.

## Viewing roles

`@role` and `@role/list` list every role, highest priority first. `@role <role>` shows one role: its priority, colour, and what it allows and denies. `@role/scopes` lists every permission, with the narrower permissions each umbrella permission covers.

`@role/player` shows your own roles and where each comes from, the overrides on you and on your account, and the permissions you hold and lack. `@role/player <object>` shows another object's, and needs the `players.view` or `roles.admin` permission, or that you may examine the object. `examine` also shows an object's roles and overrides.

Anyone may list roles and look at one.

## Creating and editing roles

`@role/create <role>` makes a new role at priority 1 that allows nothing; give it a display name with `=<display name>`. Then use `@role/allow` to say what it grants and `@role/priority` to place it.

`@role/allow`, `@role/deny` and `@role/clear` set each listed permission on the role to Allow, Deny or neither. `@role/rename`, `@role/color` and `@role/priority` change those fields; `@role/color <role>=none` removes the colour. `@role/delete` removes a role and takes it away from everything that held it.

The system roles (`everyone`, `guest`, `player`, `builder`, `royalty`, `wizard` and `god`) cannot be created, deleted or moved, but what they allow can be edited like any other role.

Examples:
```sharp
@role/create storyteller=Storyteller
@role/priority storyteller=14
@role/allow storyteller=wiki.delete media.admin
```

## Assigning roles

An object holds the roles assigned to it. A character linked to an account also holds the account's roles, on every character of that account and in the portal.

`@role/assign <object>=<role>` gives the role to the object itself, and `@role/unassign` takes it away. Add `/account` to give or take the role on the account `<player>` is linked to instead.

`@set <object>=WIZARD` and `@set <object>=ROYALTY` assign the `wizard` and `royalty` roles, and `@power <object>=Builder` and `=Guest` the `builder` and `guest` roles, under the same rules.

Three roles are never assigned: every object holds `everyone`, every player that is not a guest holds `player`, and player #1 holds `god`.

Examples:
```sharp
@role/assign Ariel=moderator
@role/assign/account Ariel=helper
@role/unassign Ariel=moderator
```

## Overrides

An override sets one permission on one holder, and beats every role that holder has. `@role/deny/object Twink=wiki.edit` takes wiki editing away from that character even though the `player` role allows it, and `@role/allow/object Ariel=wiki.delete` grants it without a role. Use `/account` to set it on the player's account instead, so it applies to all their characters. `@role/clear/object` and `@role/clear/account` remove an override.

When both are set, the object's override beats the account's. `@power <object>=See_All` is an Allow override on `game.see_all`, and `@power <object>=!See_All` clears it.

An override cannot touch `administrator`, and nothing overrides a role that allows `administrator`.

Examples:
```sharp
@role/deny/object Twink=wiki.edit media.upload
@role/clear/object Twink=wiki.edit
@role/allow/account Ariel=wiki.delete
```

## Who may change what

Every change needs the `roles.admin` permission, and follows Discord's role hierarchy:

- You can create, edit, delete, assign or unassign only roles whose priority is below your own highest role.
- You can change the roles and overrides of an object or account only when its highest role is below yours. You cannot change your own, except that a holder of `game.wizard` may set or clear power permissions (`game.see_all` and the like) on itself, as a PennMUSH wizard may `@power` itself.
- You can only allow permissions you hold yourself. A holder of `game.wizard` may also allow any `game.` permission, as a PennMUSH wizard may give any power.

As in PennMUSH, an object may give a role it holds to a thing it owns, or take it away, without `roles.admin`.

Player #1 is exempt from all of these. Role priority decides only who may manage roles; it never decides who controls whom.

**See Also:**
- [roles]
- [ROLES()]
- [HASROLE()]
- [PERMISSION()]
- [@power]
- [lock keys]
