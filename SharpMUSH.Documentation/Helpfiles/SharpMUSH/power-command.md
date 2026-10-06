<!-- help-article
{
  "corpus": "help",
  "id": "power-command",
  "lookup": "@power",
  "aliases": [],
  "sections": [
    {
      "id": "power-registry-syntax",
      "heading": "Power registry syntax",
      "lookup": "@power registry syntax"
    },
    {
      "id": "power-registry-behavior",
      "heading": "Power registry behavior",
      "lookup": "@power registry behavior"
    }
  ],
  "redirects": {
    "@power2": "@power registry syntax",
    "@power3": "@power registry behavior"
  }
}
-->
# @power

`@power/list [<power name pattern>]`<br>
`@power <power>`<br>
`@power <object>=[!]<power> [[!]<power>...]`

`@power/list` lists the defined powers (see [@power]), optionally restricted to those whose names match `<power name pattern>`, a wildcard pattern; each is shown with its one-character abbreviation, if it has one. A list of standard powers with explanations is given in [@power]. When given a power name as an argument, @power displays information about that power — its name, its character, its aliases, the object types it applies to, and the permissions needed to set and reset it. It does *not* list the powers held by an object; use powers() for that.

The third form manipulates powers on objects. `@power <object>=<power>` grants the given power; `@power <object>=!<power>` revokes it. Several powers may be given at once, separated by spaces, and each may independently carry the `!` prefix. Powers cannot be granted to players set UNREGISTERED, and only God may alter God's powers.

The standard powers are permissions (see [roles flags]): granting See_All sets an Allow override on `game.see_all` on the object, and revoking it clears the override. Builder and Guest assign and remove the `builder` and `guest` roles. These follow the rules of [@role rank], so a Wizard may grant any of them to itself or to an object whose roles are below its own. A power that the object still holds afterwards, through a role or its account, is reported. A power added with `@power/add` is stored on the object, and only Wizards may grant it.

God can add, delete, and otherwise manipulate power definitions. See help @power registry syntax for these commands.


::: seealso
- [POWERS()]
- [@flag]
- [roles]
:::

## Power registry syntax

`@power/add <power>=<alias>`<br>
`@power/delete <power>`<br>
`@power/alias <power>=<alias list>`<br>
`@power/letter <power>[=<letter>]`<br>
`@power/restrict <power>=<permissions>`<br>
`@power/type <power>=<type(s)>`<br>
`@power/enable <power>`<br>
`@power/disable <power>`<br>
`@power/decompile <power>`

These commands manipulate power definitions. Only God may use them, with the exception of `/decompile`, which only reads.
- /disable disables a power, making it invisible and unusable
- /enable re-enables a disabled power
- /alias replaces the aliases of an existing power with the space- or comma-separated list given; an empty list clears them. Like PennMUSH, the standard powers answer to PennMUSH's aliases for them: `tel_anywhere` for Tport_Anywhere, `@wall` and `wall` for Announce
- /letter changes or removes the single-letter abbreviation of an existing power (see below)
- /restrict changes power permissions (see help @power registry behavior)
- /type changes power type(s) (see help @power registry behavior)
- /delete deletes a power completely, removing it from all objects in the database and the removing it permanently from the power table. It requires the exact power name or alias to be used. Be very very careful with this.
- /decompile shows a power's full definition

`@power/letter <power>=<letter>` gives `<power>` a one-character abbreviation, which is shown beside its name in `@power/list` and on the `Character:` line of `@power <power>`. `@power/letter <power>`, with no `=`, clears it again. The letter must be a single character, and it is case sensitive: `W` and `w` are different letters. Two powers that could apply to the same type of object may not share a letter — `@power/letter` will name the power that already holds it and change nothing. Two powers with no type in common may share one.

System powers cannot be deleted, disabled, or redefined, and that includes their letters: all of the built-in powers start out with no letter and keep it that way. `/letter` applies to powers you added yourself with `@power/add`.

## Power registry behavior

`@power/add <power>=<alias>` adds a new power with the given name and alias. Both are required.

A new power starts out applying to players only, with no single-letter abbreviation, settable and resettable by Wizards. Adjust it afterwards with:

`@power/letter <power>=<letter>` — the power's one-character abbreviation, which must not collide with that of another power applying to the same object type(s). A power with no letter does not appear in a list of power characters, but can still be tested for by name.<br>
`@power/type <power>=<type(s)>` — the comma- or space-separated list of types the power applies to: one or more of 'room', 'thing', 'player', 'exit'.<br>
`@power/restrict <power>=<permissions>` — the list of permissions governing who can set, see and reset the power. See [flag permissions] for details.

Powers added with `@power/add` are stored in the database, and do not need to be re-added at startup. They are treated exactly as any other power in the server, except that they are never system powers and so remain deletable and disableable.
