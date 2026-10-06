<!-- help-article
{
  "corpus": "help",
  "id": "flag-command",
  "lookup": "@flag",
  "aliases": [],
  "sections": [
    {
      "id": "adding-flags",
      "heading": "Adding flags",
      "lookup": "@flag adding flags"
    }
  ],
  "redirects": {
    "@flag2": "@flag adding flags"
  }
}
-->
# @flag

`@flag <flag name>`<br>
`@flag/list [<flag name pattern>]`<br>
`@flag/add <flag name>=[<letter>], [<type(s)>], [<setperms>], [<unsetperms>]`<br>
`@flag/delete <flag name>`<br>
`@flag/alias <flag name>=<alias>`<br>
`@flag/letter <flag name>[=<letter>]`<br>
`@flag/restrict <flag name>=[<setperms>], [<unsetperms>]`<br>
`@flag/type <flag name>=<type(s)>`<br>
`@flag/enable <flag name>`<br>
`@flag/disable <flagname>`<br>
`@flag/decompile [<pattern>]`

This command manipulates the list of flags in the database. With no switches, the command displays information about the given flag, including aliases and permissions. `@flag/list` lists names of enabled flags, and may be given a wildcarded pattern to restrict which names it will show.

All other switches to this command are restricted to God:
- /disable disables a flag, making it invisible and unusable
- /enable re-enables a disabled flag
- /alias adds a new alias for an existing flag; use !`<alias>` to delete one.
- /letter changes or removes a single-letter alias for an existing flag.
- /restrict changes flag permissions (see help @flag adding flags)
- /type changes flag type(s) (see help @flag adding flags)
- /delete deletes a flag completely, removing it from all objects in the database and then removing it permanently from the flag table. It requires the exact flag name or alias to be used. Be very very careful with this.
- /decompile prints out a list of @flag/add commands needed to recreate the flag table on another MUSH. If `<pattern>` is given, only flags whose names match that wildcard pattern are shown.


::: seealso
- [flags]
- [@set]
- [@power]
- [flag permissions]
:::

## Adding flags

`@flag/add` is used to add a new flag with the given name. Arguments other than the flag name are optional:

`<letter>` gives the flag's one-letter abbreviation, which must not conflict with the one-letter abbreviation of another flag that could be applied to the same object type(s). It defaults to none, which means it won't appear in a list of flag characters but can still be tested for with hasflag(), andlflags(), and orlflags().<br>
`<type>` specifies the space-separated list of types to which the flag applies, and may be 'any' (the default) or one or more of 'room', 'thing', 'player', or 'exit'.<br>
`<setperms>` specifies the space-separated list of permissions for who can set and/or see the flag. See [flag permissions] for details. It defaults to 'any'<br>
`<unsetperms>` specifies the space-separated list of permissions for who can clear the flag on an object they control. It defaults to whatever `<setperms>` is given, or 'any'.

Flags added with `@flag/add` are saved with the database when it is dumped, and do not need to be re-added at startup. They are treated exactly as any other flag in the server.
