<!-- help-article
{
  "corpus": "help",
  "id": "config-tiny-command",
  "lookup": "@config tiny",
  "aliases": [],
  "sections": [
    {"id": "pennmush", "heading": "PennMUSH options", "lookup": "@config tiny pennmush"},
    {"id": "tinymush", "heading": "TinyMUSH options", "lookup": "@config tiny tinymush"},
    {"id": "tinymux", "heading": "TinyMUX functions", "lookup": "@config tiny tinymux"},
    {"id": "tinymux-commands", "heading": "TinyMUX commands and attributes", "lookup": "@config tiny tinymux commands"},
    {"id": "rhostmush", "heading": "RhostMUSH functions", "lookup": "@config tiny rhostmush"},
    {"id": "rhostmush-flags", "heading": "RhostMUSH flags and names", "lookup": "@config tiny rhostmush flags"}
  ]
}
-->
# @config tiny

Options that make SharpMUSH behave like PennMUSH, TinyMUSH, TinyMUX or RhostMUSH, for code written for those servers. `@config compatibility` lists them with their values, the servers each emulates and the imports that set it. `help <option>` shows what one does; [compatibility config] has them all.

- [@config tiny pennmush]: parenthesis groups, HTTP q-registers and @mapsql order
- [@config tiny tinymush]: null strings, booleans, trim() and @pemit
- [@config tiny tinymux]: TinyMUX logic, justification, text order, loop tokens and arguments
- [@config tiny tinymux commands]: TinyMUX /quiet, @function, attribute flags and spaces
- [@config tiny rhostmush]: the `rhost_` options
- [@config tiny rhostmush flags]: protection flags, the SIDEFX flag and `dialect`

## PennMUSH options

- `paren_groups=<boolean>`: Does an unescaped `(` that starts no function call open a literal group, as in PennMUSH? Its commas are text and its `)` does not close the call around it, so `cat(x,(a,b)c)` has two arguments. When off, escape literal parentheses inside function arguments with `\(` `\)` or `%(` `%)`. Importing a PennMUSH, TinyMUX or RhostMUSH database turns it on.
- `http_qregisters=<boolean>`: Does HTTP softcode also get its values in q-registers, and does @http read `%q<content-type>` and `%q<userpass>`, as in PennMUSH? Importing a PennMUSH database turns it on.
- `mapsql_sync=<boolean>`: Does @mapsql run its query inside the command, so every row is queued before what is queued after it, as in PennMUSH? Importing a PennMUSH database turns it on.

## TinyMUSH options

- `null_eq_zero=<boolean>`: Is a null string where a number is expected considered a 0? Importing a TinyMUX database turns it on.
- `tiny_booleans=<boolean>`: Use Tiny-style boolean values where only non-zero numbers are true.
- `tiny_trim_fun=<boolean>`: Are the second and third arguments to trim() reversed? Importing a TinyMUX or RhostMUSH database turns it on.
- `tiny_math=<boolean>`: Is a string where a number is expected considered a 0? Importing a TinyMUX database turns it on.
- `silent_pemit=<boolean>`: Does @pemit default to @pemit/silent?

## TinyMUX functions

- `mux_logic=<boolean>`: Do and(), or(), xor(), cand() and cor() test each argument by its leading signed integer, as TinyMUX does, and does xor() answer 1 for an odd number of true arguments? Importing a TinyMUX or RhostMUSH database turns it on.
- `mux_justify=<boolean>`: Do ljust(), rjust() and center() cut text longer than the width, as TinyMUX does? Importing a TinyMUX database turns it on.
- `mux_collation=<boolean>`: Do comp() with no type and sort() with no sort type order text as TinyMUX does, `a A b B`? Importing a TinyMUX database turns it on.
- `mux_queue_functions=<boolean>`: Do trigger() and verb() queue an attribute's actions, as TinyMUX's do? When off they return `#-1 FUNCTION DISABLED`. Importing a TinyMUX database turns it on.
- `innermost_tokens=<boolean>`: Do `##` and `#@` name the innermost iter() or @dolist, and `#$` the innermost switch, as in TinyMUX? When off, they name the outermost one they are written in, as in PennMUSH. Importing a TinyMUX database turns it on.
- `mux_arguments=<boolean>`: Do the set functions, hmac(), lwho(), channels() and textfile() take TinyMUX's arguments? Importing a TinyMUX database turns it on.

## TinyMUX commands and attributes

- `mux_quiet=<boolean>`: Does the /quiet switch of drop, get, give, goto, enter, leave and @teleport do what TinyMUX's does? Importing a TinyMUX database turns it on.
- `mux_function_caller=<boolean>`: Does a global @function made without /privileged run as its caller, as on TinyMUX? Off, it runs as the object holding it. Importing a TinyMUX database turns it on.
- `attribute_eval_flags=<boolean>`: Is an attribute set `noeval` never evaluated, and a `$`-command set `noparse` matched against the command as typed, as on TinyMUX and RhostMUSH? Importing a TinyMUX or RhostMUSH database turns it on.
- `space_compress=<boolean>`: Make each run of spaces one space? On by default; importing a `netmux.conf` or `netrhost.conf` that turns it off turns this off too.

## RhostMUSH functions

The `rhost_` options are for a game moved from RhostMUSH. Each is off by default, and off leaves the function as PennMUSH has it.

- `rhost_edit=<boolean>`: Are edit()'s fourth and fifth arguments RhostMUSH's first-only and ANSI modes, rather than another from/to pair? Importing a RhostMUSH database turns it on.
- `rhost_lattr=<boolean>`: Do lattr() and lattrp() take RhostMUSH's target, type, flags, regexp and tree arguments, rather than an output separator? Importing a RhostMUSH database turns it on.
- `rhost_list=<boolean>`: Is list() with two to five arguments RhostMUSH's loop, which tells each result to the enactor (or to a target the enactor controls) after an optional header? Its first three arguments are TinyMUX's list(). Importing a TinyMUX or RhostMUSH database turns it on.
- `rhost_printf=<boolean>`: Does printf() read RhostMUSH's `$`-fields, rather than SharpMUSH's `%` directives? Importing a RhostMUSH database turns it on.
- `rhost_lrooms=<boolean>`: Does lrooms() take any depth, with 0 following exits as far as they lead, as RhostMUSH does? It lists at most 1,000 rooms then. Importing a RhostMUSH database turns it on.
- `rhost_map_args=<boolean>`: Does map() leave %1 empty when no extra arguments are given, as RhostMUSH does, rather than passing the item's position? Importing a RhostMUSH database turns it on.

## RhostMUSH flags and names

- `protection_flags=<boolean>`: Do NOEXAMINE, NOMODIFY and NOCONNECT protect, as on RhostMUSH and TinyMUX? Importing a TinyMUX or RhostMUSH database turns it on.
- `sidefx_flag=<boolean>`: Do side-effect functions work only for an object with the SIDEFX flag or the `softcode.sidefx` permission, as on RhostMUSH? Others get `#-1 FUNCTION DISABLED`. Importing a RhostMUSH database turns it on.
- `dialect=<name>`: Whose names for functions and commands do typed input and attributes without a dialect flag also read? `rhost` reads RhostMUSH's and `mux` TinyMUX's, after SharpMUSH's own; empty reads none. See [attribute flags dialect] and [compatibility dialects]. Importing a TinyMUX or RhostMUSH database sets it.
