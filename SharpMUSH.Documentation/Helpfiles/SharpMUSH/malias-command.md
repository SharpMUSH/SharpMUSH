<!-- help-article
{
  "corpus": "help",
  "id": "malias-command",
  "lookup": "@malias",
  "aliases": [],
  "sections": [
    {
      "id": "creating-and-naming-aliases",
      "heading": "Creating and naming aliases",
      "lookup": "@malias creating and naming aliases"
    },
    {
      "id": "managing-recipients",
      "heading": "Managing recipients",
      "lookup": "@malias managing recipients"
    },
    {
      "id": "alias-permissions",
      "heading": "Alias permissions",
      "lookup": "@malias alias permissions"
    },
    {
      "id": "administrative-alias-operations",
      "heading": "Administrative alias operations",
      "lookup": "@malias administrative alias operations"
    }
  ],
  "redirects": {
    "@MALIAS2": "@malias creating and naming aliases",
    "@MALIAS3": "@malias managing recipients",
    "@MALIAS4": "@malias alias permissions",
    "@MALIAS5": "@malias administrative alias operations"
  }
}
-->
# @malias

- `@malias [<alias>]`

The @malias command is used to create, view, and manipulate @mail aliases, or lists. An alias is a shorthand way of specifying a list of players for @mail. Aliases begin with the '+' (plus) prefix, and represent a list of dbrefs; aliases may not include other aliases.

`@malias` with no arguments lists aliases available for your use, and is equivalent to `@malias/list`

`@malias` with a single argument (the name of an alias) lists the members of that alias, if you're allowed to see them. Other forms of the same command are `@malias/members <alias>` or `@malias/who <alias>`


::: seealso
- [@malias creating and naming aliases]
:::

## Creating and naming aliases

- `@malias[/create] <alias>=<player list>`
- `@malias/desc <alias>=<description>`
- `@malias/rename <alias>=<newalias>`
- `@malias/destroy <alias>`

The first form above creates a new alias for the given list of players.

`@malias/desc` sets the alias's description, which is shown when aliases are listed.

`@malias/rename` renames an alias.

`@malias/destroy` destroys the alias completely.


::: seealso
- [@malias managing recipients]
:::

## Managing recipients

- `@malias/set <alias>=<player list>`
- `@malias/add <alias>=<player list>`
- `@malias/remove <alias>=<player list>`

`@malias/set` resets the list of players on the alias to *<player list>*.

`@malias/add` adds players to the alias. Note that the same player may be on an alias multiple times.

`@malias/remove` removes players from the alias. If a player is on the alias more than once, a single remove will remove only one instance of that player.


::: seealso
- [@malias alias permissions]
:::

## Alias permissions

- `@malias/use <alias>=<perm list>`
- `@malias/see <alias>=<perm list>`

`@malias/use` controls who may use an alias. Players who may use an alias will see it in their @malias list, and can @mail to the alias.

`@malias/see` controls who may list the members of an alias.

An empty permission list allows any player. The permission list may also be a space-separated list of one or more of "owner", "members" (of the alias), and "admin".

By default, the owner and alias members may see and use the alias, but only the owner may list the members. Note that admin may always list aliases and their members, regardless of these settings, but are treated like anyone else when trying to @mail with an alias.


::: seealso
- [@malias administrative alias operations]
:::

## Administrative alias operations

- `@malias/all`
- `@malias/stat`
- `@malias/chown <alias>=<player>`
- `@malias/nuke`

`@malias/all` is an admin-only command that lists all aliases in the MUSH.

`@malias/stat` is an admin-only command that displays statistics about the number of aliases and members of aliases in use.

`@malias/chown` is a wizard-only command that changes the owner of an alias.

`@malias/nuke` is a God-only command that destroys all aliases.
