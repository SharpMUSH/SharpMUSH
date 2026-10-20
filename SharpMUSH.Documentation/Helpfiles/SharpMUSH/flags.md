<!-- help-article
{
  "corpus": "help",
  "id": "flags",
  "lookup": "flags",
  "aliases": [],
  "sections": [
    {
      "id": "inspecting-flags",
      "heading": "Inspecting flags",
      "lookup": "flags inspecting flags"
    },
    {
      "id": "object-types",
      "heading": "Object types are not flags",
      "lookup": "flags object types"
    }
  ],
  "redirects": {
    "FLAGS2": "flags inspecting flags"
  }
}
-->
# Flags

Flags give objects certain abilities or qualities. For example, a wizard player has wiz powers because s/he has the WIZARD flag set.

Some flags can only be set on certain types of objects, such as just players or just rooms. Other flags, like VISUAL, can be set on any type of object (player, room, exit, thing).

Flags can be set on an object with the `@set` command or `set()` function. To un-set a flag, use the exclamation point (`!`) before the flag name. For help on any particular flag, type **'help \<flag name\>'**.

A descriptive list of default flags is available in [FLAG LIST]. A complete list of all flags is available through `@flag/list`.

## Inspecting flags

You can see the list of flags set on an object in several ways:

1. If you are allowed to examine the object. The flags are listed in expanded word format on the line just below the object's name, after the word "Flags:"
2. Flag abbreviations follow the dbref after an object's name, as in `Chest(#5Tn)`, wherever the dbref is shown to you, such as in a room's contents when you can examine the object
3. The `lflags()` and `flags()` function will return a list of flag names and abbreviations for an object, respectively

## Object types are not flags

Note: The object type (player, thing, room or exit) is not actually a flag. See [TYPES OF OBJECTS] for more information.


::: seealso
- [examine]
- [FLAGS()]
- [HASFLAG()]
- [ORFLAGS()]: `orflags()` tests flag letters; `orlflags()` tests full flag names.
- [ANDFLAGS()]: `andflags()` tests flag letters; `andlflags()` tests full flag names.
- [TYPES OF OBJECTS]
- [TYPE()]
- [HASTYPE()]
- [@flag]
- [FLAG LIST]
- [@set]
- [SET()]
- [attribute flags]
:::
