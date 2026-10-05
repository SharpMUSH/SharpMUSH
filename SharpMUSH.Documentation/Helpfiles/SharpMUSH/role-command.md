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
      "id": "role-custom",
      "heading": "Custom permissions",
      "lookup": "@role define"
    },
    {
      "id": "role-categories",
      "heading": "Categories",
      "lookup": "@role categories"
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
`@role/categories`<br>
`@role/create <role>=<category>[/<display name>]`<br>
`@role/delete <role>`<br>
`@role/rename <role>=<display name>`<br>
`@role/color <role>=<#rrggbb|none>`<br>
`@role/priority <role>=<number>`<br>
`@role/category <role or custom permission>=<category>`<br>
`@role/allow <role>=<permission> [<permission> ...]`<br>
`@role/deny <role>=<permission> [<permission> ...]`<br>
`@role/clear <role>=<permission> [<permission> ...]`<br>
`@role/assign[/account] <object>=<role>`<br>
`@role/unassign[/account] <object>=<role>`<br>
`@role/allow/object <object>=<permission> [<permission> ...]` (and `/deny`, `/clear`)<br>
`@role/allow/account <player>=<permission> [<permission> ...]` (and `/deny`, `/clear`)<br>
`@role/define <permission>=<category>[/<description>]`<br>
`@role/undefine <permission>`<br>
`@role/category/create <category>=<description>`<br>
`@role/category/describe <category>=<description>`<br>
`@role/category/rename <category>=<new name>`<br>
`@role/category/delete <category>`

@role shows and manages roles: named sets of permissions that decide what an object may do, in the game and in the web portal. See [roles] for how roles combine.

A role is named by its short name (`moderator`), which is 1 to 32 lowercase letters, digits, `-` and `_`. Its display name, category, colour and priority can change; its short name cannot. A display name is up to 32 characters a player name may use.

Every role and custom permission is in a category, and the category must exist first (see [@role categories]). The portal's Roles page groups them by it. The system roles start in `System`, and the starter roles in `Staff`.

## Viewing roles

`@role` and `@role/list` list every role, highest priority first, with its category. `@role <role>` shows one role: its category, priority, colour, and what it allows and denies. `@role/scopes` lists every permission, with the narrower permissions each umbrella permission covers, and then the game's custom permissions by category.

`@role/player` shows your own roles and where each comes from, the overrides on you and on your account, and the permissions you hold and lack. `@role/player <object>` shows another object's, and needs the `players.view` or `roles.admin` permission, or that you may examine the object. `examine` also shows an object's roles and overrides.

Anyone may list roles and look at one.

## Creating and editing roles

`@role/create <role>=<category>` makes a new role in that category at priority 1 that allows nothing; give it a display name with `=<category>/<display name>`. Then use `@role/allow` to say what it grants and `@role/priority` to place it.

`@role/allow`, `@role/deny` and `@role/clear` set each listed permission on the role to Allow, Deny or neither. `@role/rename`, `@role/category`, `@role/color` and `@role/priority` change those fields; `@role/color <role>=none` removes the colour. `@role/delete` removes a role and takes it away from everything that held it.

The system roles (`everyone`, `guest`, `player`, `builder`, `royalty`, `wizard` and `god`) cannot be created, deleted or moved, but what they allow can be edited like any other role.

Examples:
```sharp
@role/create storyteller=Staff/Storyteller
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

## Custom permissions

A game can add permissions of its own for its softcode to check. `@role/define scene.close=Staff/Finish any scene` defines `scene.close` in the `Staff` category; from then on it is allowed and denied like any built-in permission, with `@role/allow`, overrides and the portal's role editor. Softcode asks `permission(%#,scene.close)` or locks with `PERM^scene.close`, and `@command/restrict` and `@function/restrict` accept it.

A name is two or more parts joined by `.`, each of lowercase letters, digits and `_`, at most 64 characters. It cannot be a built-in permission, or start with `game.`, `control.` or `protect.`. Defining a name again changes its category and description; `@role/category <permission>=<category>` changes only the category.

`@role/undefine <permission>` removes it, and every role and override that set it. It needs the right to grant the permission.

A new custom permission is held only by #1 and holders of `administrator` until a role or override allows it. A holder of `game.wizard` may allow any custom permission.

Examples:
```sharp
@role/define scene.close=Staff/Finish any scene
@role/allow helper=scene.close
think permission(*Ariel,scene.close)
```

## Categories

A category groups roles and custom permissions, and has a description of up to 200 characters. A new game has two: `System`, for the system roles, and `Staff`. `@role/categories` lists every category, with how many roles and permissions it holds and its description.

`@role/category/create <category>=<description>` makes one. A name is 1 to 32 characters a player name may use, without `/`; `valid(rolecategory, <name>)` checks one. Names are matched without regard to case. Naming a category that does not exist in `@role/create`, `@role/define` or `@role/category` is refused, with a reminder to create it first.

`@role/category/describe` changes the description, and `@role/category/rename` the name, taking everything in the category along. `@role/category/delete` removes an empty category; move what is in it elsewhere first. All of these need the `roles.admin` permission.

Examples:
```sharp
@role/category/create Scenes=People who run and close scenes
@role/create closer=Scenes/Scene closer
@role/category/rename Scenes=Scene staff
```

## Who may change what

Every change needs the `roles.admin` permission, and follows Discord's role hierarchy:

- You can create, edit, delete, assign or unassign only roles whose priority is below your own highest role.
- You can change the roles and overrides of an object or account only when its highest role is below yours. You cannot change your own, except that a holder of `game.wizard` may set or clear power permissions (`game.see_all` and the like) on itself, as a PennMUSH wizard may `@power` itself.
- You can only allow permissions you hold yourself. A holder of `game.wizard` may also allow any `game.` permission, as a PennMUSH wizard may give any power, and any custom permission.

As in PennMUSH, an object may give a role it holds to a thing it owns, or take it away, without `roles.admin`.

Player #1 is exempt from all of these. Role priority decides only who may manage roles; it never decides who controls whom.

**See Also:**
- [roles]
- [ROLES()]
- [HASROLE()]
- [PERMISSION()]
- [@power]
- [lock keys]
