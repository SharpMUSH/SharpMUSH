<!-- help-article
{
  "corpus": "help",
  "id": "editor-command",
  "lookup": "@editor",
  "aliases": [],
  "sections": [
    {"id": "commands", "heading": "Typing and editor commands", "lookup": "@editor commands"},
    {"id": "saving", "heading": "Saving and quitting", "lookup": "@editor saving"}
  ]
}
-->
# @editor

`@editor <object>/<attribute>[=function|command]`

The optional bundled `softcode-editor` package registers this command with `@command` and implements it entirely in softcode over [@input]. Install that package from the bundled package source first. The player must control the target. Existing `FUNSYNTAX` and `CMDSYNTAX` attributes select their own syntax; give `=function` or `=command` for a new or unflagged attribute.

- [@editor commands]: what a typed line does, and the dot commands
- [@editor saving]: syntax errors, save conflicts, and leaving the editor

## Typing and editor commands

Ordinary input appends one literal line. Nothing in a line is evaluated. A line beginning with `..` inserts one leading dot, so `..s` adds the source line `.s`. The editor commands are:

| Command | Action |
|---------|--------|
| `.s` | Check syntax and save, remaining in the editor. |
| `.sq` | Check syntax, save, and quit. |
| `.q` | Quit, when nothing is unsaved. |
| `.q!` | Quit and discard unsaved changes. |
| `.c` | Check syntax without saving. |
| `.l` | List the buffer with line numbers. |
| `.u` | Undo the last appended line; a second `.u` puts it back. |
| `.h` | Show the command summary. |

## Saving and quitting

A syntax error keeps the editor open and leaves the stored attribute unchanged; each error names its line and column, as `.l` numbers them. A best-effort conflict check also refuses a save when the attribute or its syntax flag changed after the editor opened it or last saved it. The editor stays open until one of its quit commands: when [@input]'s hour runs out it opens again with the draft as it was. `@input/cancel` remains the unconditional escape and saves nothing. Finish active editor sessions before upgrading, rolling back or uninstalling the package.

::: seealso
- [@input]
- [@edit]
- [attribute flags]
:::
