<!-- help-article
{
  "corpus": "help",
  "id": "addcommand-command",
  "lookup": "@addcommand",
  "aliases": [
    "@delcommand",
    "@listcommands",
    "@dialect_addcommand",
    "@dialect_delcommand",
    "@dialect_listcommands"
  ],
  "sections": []
}
-->
# @addcommand

`@addcommand <command>=<object>/<attribute>`<br>
`@delcommand <command>[=<object>/<attribute>]`<br>
`@listcommands`

@addcommand makes `<command>` a command of the game's own, run by the attribute's $-command, matched against the whole line. Adding it again changes the attribute. Over a built-in, the attribute runs first and a line it does not match runs the built-in, which is also kept as `__<command>`.

@delcommand removes it, given an object and attribute only if it runs that one, and gives a built-in its name back. @listcommands lists what each added command runs.

They are God's, built on [@command] /add and [@hook] /override, and last until restart, so add them in God's @startup. They are TinyMUX's, reached under the `mux` dialect (see [compatibility config]); their own names are @dialect_addcommand, @dialect_delcommand and @dialect_listcommands.

```sharp
> &CMD Gadget=$hello *:@pemit %#=Hello, %0!
> @set Gadget=!NO_COMMAND
> @addcommand hello=Gadget/CMD
HELLO now runs #12/CMD.
> hello world
Hello, world!
```

::: seealso
- [@command]
- [@hook]
:::
