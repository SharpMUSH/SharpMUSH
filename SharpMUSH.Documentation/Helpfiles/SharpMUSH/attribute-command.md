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
      "id": "attribute-index-building",
      "heading": "Building and showing an index",
      "lookup": "@attribute index building"
    },
    {
      "id": "attribute-watch",
      "heading": "Watched attributes",
      "lookup": "@attribute watch"
    },
    {
      "id": "attribute-watch-arguments",
      "heading": "What a watch handler gets",
      "lookup": "@attribute watch arguments"
    },
    {
      "id": "attribute-watch-chains",
      "heading": "Watch chains and requirements",
      "lookup": "@attribute watch chains"
    },
    {
      "id": "attribute-watch-example",
      "heading": "Watch example",
      "lookup": "@attribute watch example"
    },
    {
      "id": "attribute-watch-gauge",
      "heading": "Gauge example",
      "lookup": "@attribute watch gauge"
    },
    {
      "id": "attribute-packages",
      "heading": "Attribute entries from packages",
      "lookup": "@attribute packages"
    },
    {
      "id": "attribute-packages-ownership",
      "heading": "Whose entry it is, and existing copies",
      "lookup": "@attribute packages ownership"
    },
    {
      "id": "attribute-packages-index",
      "heading": "Indexes and watches from packages",
      "lookup": "@attribute packages index"
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

## Building and showing an index

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

## Watched attributes

`@attribute/watch <attrib>[=<pattern>]`<br>
`@attribute/watch <attrib>=`

`@attribute/watch` makes `<attrib>` a watched attribute. From then on, every time a copy of it on any object is set, cleared or wiped, by any command or function, the event handler object runs each of its own attributes whose name matches `<pattern>`. `<pattern>` is one or more attribute wildcards separated by spaces, as lattr() takes them: `*` matches within one level of an attribute tree and `**` across levels. An attribute that matches more than one of them runs once.

Without `=<pattern>`, the pattern is ``WATCH`<attrib> WATCH`<attrib>`*``: the handler's ``WATCH`<attrib>`` and every attribute one level below it. Each package can then keep a handler of its own, such as ``WATCH`HP`GAUGE``, without editing another's. `@attribute/watch <attrib>=` with nothing after the `=` stops watching it.

## What a watch handler gets

Each matching attribute is queued on its own, as an event's attribute is, and runs as the event handler with its permissions. It gets:

- `%0` or `%<objid>`: the object the attribute is on.
- `%1` or `%<attribute>`: the attribute's full name, in upper case (HP, or STATS\`HP).
- `%2` or `%<old>`: the value it had. A new attribute's is empty.
- `%3` or `%<new>`: the value it has now. A cleared or wiped attribute's is empty.
- `%4` or `%<enactor>`: whoever changed it, which `%#` is too, or #1 when the server changed it for its own bookkeeping.

The values keep their colour. Setting an attribute to the value it already has still runs the handlers, and a `@wipe` runs them once for each watched attribute it removes. Only the name that is watched counts: watching STATS does not watch STATS\`HP. Attributes nobody watches cost nothing, and loading a PennMUSH database runs nothing. The handlers run after the change has happened, so they cannot stop it, and stopevent() does nothing in them.

## Watch chains and requirements

A handler that changes a watched attribute runs the handlers again, and so does anything it queues with `@trigger`, `@wait` or `@force`. To keep a handler that changes what it heard about, or two handlers that change each other's attributes, from running for ever, a chain stops after 10: a change made by the tenth handler is still made, but runs nothing, and the server logs a warning. A change typed by a player or made by any other code starts a new chain.

As with `/limit` and `/enum`, `<attrib>` must already be a standard attribute: add it with `@attribute/access` first. `@attribute <attrib>` shows the pattern, attrwatch() returns it to softcode (see [attrwatch()]), and `@attribute/decompile` writes the `@attribute/watch` line that makes it again. Only those with the config.admin permission can watch an attribute.

## Watch example

```sharp
> @attribute/access HP=none
> &HP me=20
> @attribute/watch HP
> &WATCH`HP`LOG #9=@pemit %<objid>=HP went from %<old> to %<new>.
> &HP me=15
```
Output: after the last line you are told "HP went from 20 to 15."

## Gauge example

A client such as Mudlet can draw a health bar from the GMCP package Char.Vitals. Instead of checking every player's HP over and over, watch HP and MAXHP and send the bar whenever one of them changes:
```sharp
> @attribute/watch HP=WATCH`VITALS`*
> @attribute/watch MAXHP=WATCH`VITALS`*
> &WATCH`VITALS`GAUGE #9=think oob(num(%<objid>),Char.Vitals,json(object,hp,json(number,default(%<objid>/HP,0)),maxhp,json(number,default(%<objid>/MAXHP,0))))
> &MAXHP me=40
> &HP me=25
```
Output: after the last line your client is sent Char.Vitals {"hp":25,"maxhp":40}. The line before it sent one too, with an hp of 0.

Both attributes run the same handler, so it reads both values again rather than using `%<new>`, and each message carries the whole gauge. It is sent to the player whose attribute changed, and oob() sends nothing to a connection that did not agree to GMCP. If the attribute is on a thing rather than a player, nobody is connected as it and nothing is sent.

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

## Whose entry it is, and existing copies

An entry the game already has is the game's: the package uses it and never changes it, and if its limit or enum differs from the manifest's, the package cannot be installed until the two agree. An entry the package created follows it: an upgrade changes only what the new version changed, so your own `@attribute/access` on it survives, and uninstalling the package deletes the entry. Values already stored on objects stay.

Default flags reach new copies of the attribute only. An entry with `retroactive: true` also gives them to every copy already on an object, like `@attribute/access/retroactive` but keeping each copy's owner; the portal's review screen shows how many copies change and asks you to confirm before it applies.

## Indexes and watches from packages

From format 1.8 an entry can also carry an `index:`, as `@attribute/index` would make it: `index: true` for the default, or the option words, such as `index: numeric type:thing`. An older manifest's `index:` is ignored. Installing the package builds the index in the background, and the review screen says how many objects it builds over. A game entry without the same index blocks the install until you index it.

From format 1.9 an entry can also carry a `watch:`, as `@attribute/watch` would set it: `watch: true` for the default pattern, or the pattern itself, such as ``watch: WATCH`HP`*``. An older manifest's `watch:` is ignored. A game entry without the same watch blocks the install until you watch it that way. An upgrade that drops `watch:` stops watching the attribute, and uninstalling the package deletes the entry with its watch.

Example, in a package's manifest:
```
format: 1.9
attributes:
  HP:
    watch: true
```
Output: once the package is installed, ``@attribute HP`` shows ``Watch: WATCH`HP WATCH`HP`*``, and the package's ``WATCH`HP`<name>`` attribute on the event handler runs whenever an HP changes.

::: seealso
- [events]
- [indexed()]
- [attrwatch()]
- [attributes]
- [attribute flags]
- [@set]
- [@atrchown]
- [@atrlock]
- [@list]
:::
