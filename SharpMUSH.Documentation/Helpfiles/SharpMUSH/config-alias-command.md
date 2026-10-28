<!-- help-article
{
  "corpus": "help",
  "id": "config-alias-command",
  "lookup": "@config alias",
  "aliases": [],
  "sections": []
}
-->
# @config alias

Other names for functions and commands. Each option maps a name to the aliases it has, one entry per line in `@config alias`. Neither can be changed with `@config/set`; edit them on the web portal's Aliases settings page.

- `function_aliases`: Function names and their aliases, such as `modulo` with `mod modulus`.
- `command_aliases`: Command names and their aliases, such as `LOOK` with `l`. A name may carry switches: `@FORCE/INPLACE` with `@sudo` makes `@sudo <object>=<command>` run `@force/inplace`, and switches typed after the alias are added to the ones it carries.

An alias added with `@command/alias` lasts until the server restarts and is not stored here: see [@command].
