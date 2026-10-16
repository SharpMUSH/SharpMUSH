<!-- help-article
{
  "corpus": "help",
  "id": "list-function",
  "lookup": "list()",
  "aliases": [],
  "sections": [
    {"id": "rhost", "heading": "RhostMUSH form", "lookup": "list rhost"}
  ]
}
-->
# list()
`list(<option>[, <type>])`<br>
`list(<list>, <eval>[, <delimiter>][, <header>][, <target>])`

  This is the function-equivilent of the @list command, and lists some useful information about the MUSH. `<option>` can be one of:

  motd        : Returns the current @motd<br>
  wizmotd     : Returns the current @motd/wizard. Wiz/Roy only.<br>
  downmotd    : Returns the current @motd/down. Wiz/Roy only.<br>
  fullmotd    : Returns the current @motd/full. Wiz/Roy only.<br>
  functions   : Returns a list of all built-in functions and @functions.<br>
  commands    : Returns a list of all built-in commands and @commands.<br>
  attribs     : Returns all standard attributes.<br>
  locks       : Returns the built-in lock types. Similar to llocks().<br>
  flags       : Returns all flags. Similar to lflags().<br>
  powers      : Returns all @powers.

  "commands"/"functions" return both built-in and local commands/functions by default. You can specify a `<type>` of either "builtin", "local" or "all" to limit this if you wish.

- [list rhost]: the second form, used with rhost_list on, and examples

## RhostMUSH form

  With the `rhost_list` @config option on, list() is the second form instead, which comes from RhostMUSH; its first three arguments are TinyMUX's list(). `<eval>` is evaluated for each element of `<list>`, as by iter(), and each result is told to you on a line of its own rather than returned. list() itself returns nothing. `<delimiter>` separates the elements and is a space when left out. A `<header>` that is not empty is told once, before the first result. A `<target>` you control hears the header and the results instead of you; a target you do not control, or one that is not found, is ignored. An empty list tells nothing, not even the header. See [compatibility config].

  Examples, with `rhost_list` on:

    > think list(red blue,Color #@: ##)<br>
    Color 1: red<br>
    Color 2: blue<br>
    > think list(a|b,[strlen(##)],|,Lengths:)<br>
    Lengths:<br>
    1<br>
    1


::: seealso
- [@list]
- [FLAGS()]
- [LFLAGS()]
- [CONFIG()]
- [FUNCTIONS()]
- [@motd]
- [@motd]
:::
