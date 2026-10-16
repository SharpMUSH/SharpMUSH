<!-- help-article
{
  "corpus": "help",
  "id": "palette",
  "lookup": "palette entries",
  "aliases": [
    "palette",
    "command palette",
    "palette_pins"
  ],
  "sections": [
    {
      "id": "palette-rising",
      "heading": "What the palette shows first",
      "lookup": "palette rising"
    },
    {
      "id": "palette-keys",
      "heading": "Palette entry keys",
      "lookup": "palette keys"
    },
    {
      "id": "palette-display-keys",
      "heading": "Palette display keys",
      "lookup": "palette display keys"
    },
    {
      "id": "palette-offered",
      "heading": "Which entries a player is offered",
      "lookup": "palette offered"
    },
    {
      "id": "palette-input",
      "heading": "Palette entries for @input",
      "lookup": "palette input"
    },
    {
      "id": "palette-pins",
      "heading": "Palette pins",
      "lookup": "palette pins"
    }
  ]
}
-->
# Palette Entries

The portal's command palette (Ctrl+K) runs game commands as well as going to pages. A command gets there through a palette entry: an attribute `PALETTE`<name>` on the object that holds the command, whose value is a JSON object saying what the row is called and what line it sends.

```sharp
> &CMD`JOIN Scene Board=$+join *:@pemit %#=You join scene %0.
> &PALETTE`JOIN Scene Board={"label":"Join the scene here","line":"+join <scene>","when":["scene_here"],"info":"Join the scene in this room."}
> &CMD`SCENES Scene Board=$+scenes:@pemit %#=Scene 12 is running here.
> &PALETTE`SCENES Scene Board={"label":"Scenes here","line":"+scenes"}
```

The value is data and is never evaluated, except the `lock`. An object offers at most 32 entries and one palette at most 400; the farthest are left out first. Someone who may examine an object sees, below the empty palette, which of its entries are not valid and why.

::: seealso
- [palette rising]
- [palette keys]
- [palette display keys]
- [palette offered]
- [palette input]
- [palette pins]
- [@context]
- [context()]
:::

## What the palette shows first

With nothing typed, the palette shows, in order: lines for the `@input` session the player is in, urgent commands, the player's pins, what this page and their context put first, commands marked `always`, then the portal's pages. Typing searches all of them, and the built-in commands the player may use. Choosing a row runs its line as if the player had typed it, so every check the command makes still applies.

What rises comes from the player's context stack (see [@context]). Typing `+scenes` once puts Scene Board's system on the stack, and its entries rise. Pushing `scene_here` with a `scene` value raises "Join the scene here" with the line filled in, `+join 12`. context() returns the same commands to softcode.

## Palette entry keys

| Key | Meaning |
|---|---|
| `label` | Required. What the row says, plain text. |
| `line` | Required. The command line. Each `<slot>` is asked for before the line runs, unless a context filled it; `\<` is a plain `<`. |
| `action` | `run` (the default) or `insert`, which puts the line into the Play composer for the player to finish. |
| `lock` | A lock, checked with the player as enactor. The entry is offered only when it passes. |
| `always` | `true` for a command useful anywhere, such as `+who`. It is listed under Always. |
| `when` | Context names the entry needs, all of them. While they are on the stack it rises, and their values fill its slots of the same name. Without `when`, the entry rises with its object's own system. |
| `urgent` | `true` lists it above the pins while its `when` contexts are on the stack. |
| `context` | `input` (see [palette input]), or a portal page: `play`, `scene`, `wiki`, `editor`, `app:<slug>`. The entry is listed under Here on that page. |

## Palette display keys

| Key | Meaning |
|---|---|
| `info` | One line saying what the command does, shown under the row. A raising context's own info takes its place. |
| `group` | A word shown at the end of the row. |
| `icon` | A Material icon name, such as `Article`. |
| `keywords` | More words the search matches, space-separated or a JSON array. |
| `help` | A help topic. Typing offers a "Help on" row for it. |
| `order` | A number that sorts the object's entries; lower first. |

A value that is not one of these or of [palette keys] is ignored. An entry that is not valid says why: `not a JSON object`, `no label`, `no line`, `action is neither run nor insert`.

## Which entries a player is offered

An entry is offered when all of these hold:

1. Its object is somewhere a typed `$`-command would look: the player, what they carry, the room and its contents, the room's zone, the player's zone, or the master room. The player must be able to see it.
2. The object hears commands (not NO_COMMAND or HALT), and its `@lock/command` and `@lock/use` pass for the player.
3. An entry on a player is offered only to that player, and rule 2 does not apply to it, so `&PALETTE`WAVE me={"label":"Wave","line":"pose waves."}` is a personal shortcut.
4. A line whose first word is a built-in command needs the player to be allowed that command.
5. Its `lock` passes.

Things and players start NO_COMMAND; `@set <object>=!no_command` lets a thing's entries be offered.

## Palette entries for @input

While an `@input` session takes the player's lines, the palette lists first the entries marked `"context":"input"` on the objects the session sends lines to, even when those objects are nowhere else in reach, and a Leave row when the exit is a plain word. These rows need the player's terminal to be connected, since the session takes lines from it. The entry's `lock` still applies, so a reader can show the writing keys only while writing.

```sharp
> &PALETTE`DONE Notepad={"label":"Save the note","line":".done","context":"input"}
> &PALETTE`UNDO Notepad={"label":"Drop the last line","line":".undo","context":"input"}
```

## Palette pins

A player pins a row with the pin button or Alt+P. Pins are kept in order in `PALETTE_PINS` on the character, space-separated: an entry is `<objid>/<name>`, a built-in `cmd:<command>`, a portal page `go:<path>`. A pin that is out of reach where the player is now is not shown, and stays in the list.

```sharp
> &PALETTE_PINS me=#15:1792044786423/SCENES cmd:page go:/wiki
```
