<!-- help-article
{
  "corpus": "help",
  "id": "cpattr-command",
  "lookup": "@cpattr",
  "aliases": ["@mvattr"],
  "sections": [
    {"id": "examples", "heading": "Examples", "lookup": "@cpattr examples"},
    {"id": "patterns", "heading": "Copying by pattern", "lookup": "@cpattr patterns"}
  ]
}
-->
# @cpattr

`@cpattr[/noflagcopy] <obj>/<attr>=<obj1>[/<attr1>][, ..., <objN>[/<attrN>]]`<br>
`@mvattr[/noflagcopy] <obj>/<attr>=<obj1>[/<attr1>][, ..., <objN>[/<attrN>]]`<br>
`@cpattr[/noflagcopy] <obj>/<attribute pattern>=<obj1>[, ..., <objN>]`<br>
`@mvattr[/noflagcopy] <obj>/<attribute pattern>=<obj1>[, ..., <objN>]`

@cpattr copies the `<attr>` attribute from `<obj>` to `<obj1>` (and any other objects given). By default, the new attributes will have the same name as the original, but you can specify a different name to be used on each object if you wish.

@mvattr works the same, but also attempts to remove the original attribute after copying it.

Attribute flags are copied as well, unless the `/noflagcopy` switch is given. This is recommended when copying from a non-standard attribute to a standard one. `@cpattr/convert` and `@mvattr/convert` are accepted, as PennMUSH accepts them, and do nothing more than the command without the switch.

- [@cpattr examples]: copying one attribute to several objects and names
- [@cpattr patterns]: copying every attribute that matches a wildcard

## Examples

```sharp
> @cpattr box/test=box/test1, cube/random, tribble/describe
```
would check the object "box" for an attribute named TEST and then copy it to the attributes TEST1 on "box", RANDOM on the object named "cube", and DESCRIBE on the object named "tribble".
```sharp
> @cpattr box/test=cube
```
would copy the TEST attribute from "box" to TEST on "cube".

## Copying by pattern

When `<attr>` is a wildcard pattern (see [wildcards]), every attribute of `<obj>`'s own that matches it and that you can see is copied, each under its own name, so each destination names only an object. PennMUSH copies one attribute at a time; patterns are a SharpMUSH addition.
```sharp
> @cpattr box/CMD`**=cube
```
Output: one "Attribute <name> copied to 1 destination." line for each attribute under CMD on "box". Each copy on "cube" has the same name as its original.

::: seealso
- [attributes]
- [NON-STANDARD ATTRIBUTES]
- [@set]
:::
