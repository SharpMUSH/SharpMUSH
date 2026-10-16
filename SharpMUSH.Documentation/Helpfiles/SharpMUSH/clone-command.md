<!-- help-article
{
  "corpus": "help",
  "id": "clone-command",
  "lookup": "@clone",
  "aliases": [],
  "sections": [
    {"id": "details", "heading": "Placement, flags and dbrefs", "lookup": "@clone details", "aliases": ["@clone/preserve"]},
    {"id": "example", "heading": "Cloning a room with its exits", "lookup": "@clone example"}
  ]
}
-->
# @clone

`@clone <object>[=<new name>[, <dbref>]]`<br>
`@clone/preserve <object>[=<new name>[, <dbref>]]`

This command creates a copy of `<object>`. The clone will have the same name as the original unless a `<new name>` is given for it. You can only clone things, rooms and exits, not players. You must control `<object>`. The new object will be owned by the player who performs the @clone, not the owner of the original `<object>`.

Output: the clone's dbref, as `clone()` returns it.

Note: If @create is restricted or disabled, it will also restrict or disable this command.

- [@clone details]: where the clone goes, what it keeps, and choosing its dbref
- [@clone example]: cloning a room together with its exits

## Placement, flags and dbrefs

`@clone/preserve <object>[=<new name>[, <dbref>]]`

When cloning things and exits, the clone will be placed in your current location, not the location of `<object>`. When cloning rooms, the exits and contents in the room are not cloned as well.

The cloned object will have the same modification time as the original object, to make tracking revisions easier, but will have a different creation time.

Normally, the Wizard and Royalty flags, @powers and @warnings are stripped from the cloned object, but Wizards may use the `/preserve` flag to prevent this.

The clone will normally be created with the first available dbref, but Wizards and objects with the pick_dbref power may specify the `<dbref>` of a garbage object to use that instead.

## Cloning a room with its exits

To clone a room and all its exits, use code like:
```sharp
> @teleport setq(0,%L)[clone(here)]
> @dolist lexits(%q0)=@clone ##
```

::: seealso
- [@create]
- [CLONE()]
- [CREATE()]
- [@cpattr]
:::
