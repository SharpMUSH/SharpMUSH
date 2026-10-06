<!-- help-article
{
  "corpus": "help",
  "id": "attribute-command",
  "lookup": "@attribute",
  "aliases": [],
  "sections": [
    {
      "id": "attribute-registry-management",
      "heading": "Attribute registry management",
      "lookup": "@attribute registry management"
    },
    {
      "id": "registry-permissions",
      "heading": "Registry permissions",
      "lookup": "@attribute registry permissions"
    }
  ],
  "redirects": {
    "@attribute2": "@attribute registry management",
    "@attribute3": "@attribute registry permissions"
  }
}
-->
# @attribute

`@attribute <attrib>`

The @attribute command displays and modifies the MUSH's standard attributes (see "@list/attribs" for a list of them).

Since 1.8.5p1, changes to the attribute table are saved across reboots and shutdowns, and don't need to be placed in an @startup.

The first form of the command displays the full name of the attribute `<attrib>`, along with the its attribute flags, and the dbref of the object which added it to the attribute table.

## Attribute registry management

`@attribute/access[/retroactive] <attrib>=<flag list>`<br>
`@attribute/delete <attrib>`<br>
`@attribute/rename <attrib>=<new name>`<br>
`@attribute/decompile[/retroactive] [<pattern>]`

`@attribute/access` adds `<attrib>` as a new standard attribute, with the default attribute flags `<flag list>`. If `<attrib>` is already a standard attribute, this command modifies its default attribute flags. Use "none" for `<flag list>` if you don't want any default attribute flags.

If the `/retroactive` switch is given with `/access`, all existing copies of the attribute will be @atrchown'd to the player running the command, and will have its flags changed to `<flag list>`. A copy's `branch` flag, which marks that it has attributes below it, is kept. The command reports how many copies it changed; if it runs out of time before reaching every object, or some copies cannot be changed, it says how far it got, and running it again finishes the job.

`@attribute/delete` removes a standard attribute from the table.<br>
`@attribute/rename` renames a standard attribute.

Only Wizards can modify the attribute table.

`@attribute/decompile` prints out a list of @attribute/access commands needed to recreate the attribute table on another MUSH. If `/retroactive` is given, that switch will be included in the output. If `<pattern>` is given, only attributes matching `<pattern>` are decompiled.

## Registry permissions

`@attribute/limit <attrib>=<regexp pattern>`<br>
`@attribute/enum [<delim>] <attrib>=<list of choices>`

`@attribute/limit` lets you restrict all _new_ values for an attribute to those that match a regexp pattern. Case insensitive. (Use (?-i) to make your regexp case-sensitive.)

`@attribute/enum` lets you restrict all _new_ values for an attribute to match an item in a list. It will also perform partial matching on the list, much like a grab. Delimiter is optional, and defaults to a space.

### Examples
```sharp
@attribute/enum sex=male female   <-- requires 'male' or 'female' as @sex
@attribute/enum | race=Wookie|Indy 500 <- Your race can be 'wookie' or 'Indy 500'
@attribute/limit score=^\\d+$    <-- @score can only contain digits. (Remember, Penn's parser eats a \)
```


::: seealso
- [attributes]
- [attribute flags]
- [@set]
- [@atrchown]
- [@atrlock]
- [@list]
:::
