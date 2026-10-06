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
    }
  ],
  "redirects": {
    "FLAGS2": "flags inspecting flags"
  }
}
-->
# flags

Flags give objects certain abilities or qualities. For example, a wizard player has wiz powers because s/he has the WIZARD flag set.

Some flags can only be set on certain types of objects, such as just players or just rooms. Other flags, like VISUAL, can be set on any type of object (player, room, exit, thing).

Flags can be set on an object with the `@set` command or `set()` function. To un-set a flag, use the exclamation point (`!`) before the flag name. For help on any particular flag, type **'help \<flag name\>'**.

A descriptive list of default flags is available in [FLAG LIST]. A complete list of all flags is available through `@flag/list`.

## Inspecting flags

You can see the list of flags set on an object in several ways:

1. If you are allowed to examine the object. The flags are listed in expanded word format on the line just below the object's name, after the word "Flags:"
2. Flag abbreviations are also visible after the object's name in the room description, if the object is not set OPAQUE and you are not set MYOPIC
3. The `lflags()` and `flags()` function will return a list of flag names and abbreviations for an object, respectively

Note: The object type (player, thing, room, exit or garbage) is not actually a flag. See [TYPES OF OBJECTS] for more information.


::: seealso
- [examine]
- [FLAGS()]
- [HASFLAG()]
- [ORFLAGS()] — `orflags()` tests flag letters; `orlflags()` tests full flag names.
- [ANDFLAGS()] — `andflags()` tests flag letters; `andlflags()` tests full flag names.
- [TYPES OF OBJECTS]
- [TYPE()]
- [HASTYPE()]
- [@flag]
- [FLAG LIST]
- [@set]
- [SET()]
- [attribute flags]
:::
