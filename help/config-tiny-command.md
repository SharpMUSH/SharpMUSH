<!-- help-article
{
  "corpus": "help",
  "id": "config-tiny-command",
  "lookup": "@config tiny",
  "aliases": [],
  "sections": [
    {"id": "tinymush", "heading": "TinyMUSH and parser options", "lookup": "@config tiny tinymush"},
    {"id": "tinymux", "heading": "TinyMUX options", "lookup": "@config tiny tinymux"},
    {"id": "rhostmush", "heading": "RhostMUSH options", "lookup": "@config tiny rhostmush"}
  ]
}
-->
# @config tiny

Options that make SharpMUSH behave like TinyMUSH, TinyMUX or RhostMUSH, for code written for those servers.

- [@config tiny tinymush]: null strings, booleans, trim(), @pemit and parenthesis groups
- [@config tiny tinymux]: TinyMUX logic, justification, text order and loop tokens
- [@config tiny rhostmush]: the `rhost_` options and the SIDEFX flag

## TinyMUSH and parser options

- `null_eq_zero=<boolean>`: Is a null string where a number is expected considered a 0?
- `tiny_booleans=<boolean>`: Use Tiny-style boolean values where only non-zero numbers are true.
- `tiny_trim_fun=<boolean>`: Are the second and third arguments to trim() reversed?
- `tiny_math=<boolean>`: Is a string where a number is expected considered a 0?
- `silent_pemit=<boolean>`: Does @pemit default to @pemit/silent?
- `paren_groups=<boolean>`: Does an unescaped `(` that starts no function call open a literal group, as in PennMUSH? Its commas are text and its `)` does not close the call around it, so `cat(x,(a,b)c)` has two arguments. When off, escape literal parentheses inside function arguments with `\(` `\)` or `%(` `%)`. Importing a PennMUSH, RhostMUSH or TinyMUX database turns it on.

## TinyMUX options

- `mux_logic=<boolean>`: Do and(), or(), xor(), cand() and cor() test each argument by its leading signed integer, as TinyMUX does, and does xor() answer 1 for an odd number of true arguments? See [compatibility config].
- `mux_justify=<boolean>`: Do ljust(), rjust() and center() cut text longer than the width, as TinyMUX does?
- `mux_collation=<boolean>`: Do comp() with no type and sort() with no sort type order text as TinyMUX does, `a A b B`?
- `innermost_tokens=<boolean>`: Do `##` and `#@` name the innermost iter() or @dolist, and `#$` the innermost switch, as in TinyMUX? When off, they name the outermost one they are written in, as in PennMUSH.

## RhostMUSH options

The `rhost_` options below are for a game moved from RhostMUSH. Each is off by default, and off leaves the function as PennMUSH has it. See [compatibility config].

- `rhost_edit=<boolean>`: Are edit()'s fourth and fifth arguments RhostMUSH's first-only and ANSI modes, rather than another from/to pair?
- `rhost_lattr=<boolean>`: Do lattr() and lattrp() take RhostMUSH's target, type, flags, regexp and tree arguments, rather than an output separator?
- `rhost_list=<boolean>`: Is list() with two to five arguments RhostMUSH's loop, which tells each result to the enactor (or to a target the enactor controls) after an optional header? Its first three arguments are TinyMUX's list().
- `rhost_printf=<boolean>`: Does printf() read RhostMUSH's `$`-fields, rather than SharpMUSH's `%` directives?
- `rhost_map_args=<boolean>`: Does map() leave %1 empty when no extra arguments are given, as RhostMUSH does, rather than passing the item's position?
- `sidefx_flag=<boolean>`: Do side-effect functions work only for an object with the SIDEFX flag or the `softcode.sidefx` permission, as on RhostMUSH? Others get `#-1 FUNCTION DISABLED`.
