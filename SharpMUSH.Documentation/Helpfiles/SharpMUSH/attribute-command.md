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
    },
    {
      "id": "attribute-index",
      "heading": "Attribute indexes",
      "lookup": "@attribute index"
    },
    {
      "id": "attribute-packages",
      "heading": "Attribute entries from packages",
      "lookup": "@attribute packages"
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

## Attribute indexes

`@attribute/index <attrib>[=<options>]`<br>
`@attribute/index <attrib>=off`

`@attribute/index` keeps an index of the values of a standard attribute, so indexed() and lsearch()'s INDEX class find the objects holding a value without looking at every object. Only an object's own copy is indexed, never one it inherits, and an empty value or a copy flagged internal is never listed. An attribute whose default flags include internal cannot be indexed.

`<options>` are words, each optional:

- `exact`: values compare as written. Without it they compare without case.
- `numeric`: values that are numbers come first, in numeric order, so 9 comes before 10 and 05 equals 5; other values follow as text.
- `split` or `split:<delimiter>`: each word of a value (or each element, split on the one character given) is listed on its own.
- `type:<type>`: only copies on players, rooms, things or exits.

The index is built in the background, a batch of objects at a time, and goes on after a restart where it stopped. The command waits for it as long as a command may, then says how far it got, and tells you when it is ready. Until then, indexed() returns `#-1 INDEX BUILDING`. `=off` takes the index off and deletes it. Writes keep the index up to date as they happen.

`@attribute <attrib>` shows an attribute's index and whether it is ready, `@attribute/decompile` writes the `@attribute/index` line that makes it again, and `@storage` shows the room each index takes. Only those with the config.admin permission can add or remove an index.

Example:
```sharp
> @attribute/access LEVEL=none
> @attribute/index LEVEL=numeric type:player
Indexing LEVEL: numbers, then text without case, players only.
Index on LEVEL ready: 412 copies on 1530 objects indexed.
```
Output: LEVEL is now indexed for players, numbers in numeric order, and indexed(LEVEL, >=, 10) lists the players whose LEVEL is 10 or more.

## Attribute entries from packages

A package can add standard attributes to the table itself, in the `attributes:` block of its manifest (format 1.5), instead of running `@attribute` from its install code. Each entry names the attribute and gives what `@attribute` would set: default `flags`, and a `limit` or an `enum` (with a `delimiter` when a choice holds a space).

Example, in a package's manifest:
```
format: 1.5
attributes:
  JOB`STATUS:
    flags: [no_command]
    enum: [new, staff, held, closed]
```
Output: once the package is installed, ``@attribute JOB`STATUS`` shows the default flag no_command and the choices new staff held closed, and ``&JOB`STATUS me=open`` is refused because open is not one of them.

An entry the game already has is the game's: the package uses it and never changes it, and if its limit or enum differs from the manifest's, the package cannot be installed until the two agree. An entry the package created follows it: an upgrade changes only what the new version changed, so your own `@attribute/access` on it survives, and uninstalling the package deletes the entry. Values already stored on objects stay.

From format 1.8 an entry can also carry an `index:`, as `@attribute/index` would make it: `index: true` for the default, or the option words, such as `index: numeric type:thing`. An older manifest's `index:` is ignored. Installing the package builds the index in the background, and the review screen says how many objects it builds over. A game entry without the same index blocks the install until you index it.

Default flags reach new copies of the attribute only. An entry with `retroactive: true` also gives them to every copy already on an object, like `@attribute/access/retroactive` but keeping each copy's owner; the portal's review screen shows how many copies change and asks you to confirm before it applies.

::: seealso
- [indexed()]
- [attributes]
- [attribute flags]
- [@set]
- [@atrchown]
- [@atrlock]
- [@list]
:::
