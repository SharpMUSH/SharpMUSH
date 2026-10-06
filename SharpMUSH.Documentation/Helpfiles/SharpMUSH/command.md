<!-- help-article
{
  "corpus": "help",
  "id": "command",
  "lookup": "@command",
  "aliases": [],
  "sections": [
    {
      "id": "added-commands",
      "heading": "Adding commands",
      "lookup": "@command added commands"
    },
    {
      "id": "examples",
      "heading": "Added command example",
      "lookup": "@command examples"
    }
  ],
  "redirects": {
    "@command2": "@command added commands",
    "@command3": "@command examples"
  }
}
-->
# @command

`@command <command>`<br>
`@command/<switch> <command>`<br>
`@command/alias <command>=<alias>`<br>
`@command/clone <command>=<clone>`<br>
`@command/restrict <command>=<restriction> [" <error message>]`

@command can be used for adding new built-in commands, altering the way a built-in command works, and displaying information about how commands currently work.

With no switches, @command shows all sorts of interesting information about how a command is parsed. Any player can use @command with no switch, while the switches are Wizard-only.

The `/alias` switch creates an alias for `<command>`, allowing players to type `<alias>` to run `<command>`. The `/clone` switch creates a separate copy of `<command>`, which works the same initially but can be restricted, @hooked, etc, separately.

`@command/restrict` can be used to restrict who can use `<command>`. See [restrict] for more information.

Switches include:
- /add : Add a new command that does nothing, but can be @hook'd.
- /delete : Delete a command added with @command/add or /alias. God only.
- /disable : Disable a command added in the hardcode. A disabled command is not a command at all: what was typed goes on to $-commands and then Huh?.
- /enable : Re-enable a command disabled with @command/disable.

`<restriction>` is a lock, or words naming who may use the command: flag and power names, `admin` (royalty or wizard), `player`, `thing`, `room`, `exit` or `any`, `god`, `noguest`, `nogagged`, `nofixed`, each negated with `!`, and `nobody`, which disables the command. Naming a type restricts the command to it, so `@command/restrict foo=player thing` leaves it usable by players and things only, while `!player` (or `noplayer`) leaves every other type. A `<error message>` after a `"` is shown instead of "Permission denied." to anyone the restriction refuses; a bare `"` clears it.

The `/quiet` switch can be used to suppress output from @command.

Everything these switches change lasts until the server restarts. A permanent alias belongs in the `command_aliases` configuration option, and a permanent restriction in `command_restrictions`. Changing `command_restrictions` takes effect at once, and puts every command it names, before or after the change, back to the restriction it was made with before applying the new setting, so a live `@command/restrict` on one of those commands lasts only until the next restart or change to `command_restrictions`. HUH_COMMAND, @CHAT and GOTO are run by the game itself and cannot be disabled, and @command is always enabled.

## Adding commands

`@command/add` is a powerful tool that lets you create new commands which are matched before normal $-commands, and which can be set not to parse their arguments, but (via @hook) can still execute softcode like an $-command.

You can use these additional switches, along with `@command/add`, to control how the new command parses its arguments:

- /noparse : The command does not evaluate the leftside arg(s).
- /eqsplit : The parser parses leftside and rightside around =
- /lsargs : Comma-separated arguments on the left side are parsed.
- /rsargs : When used with /eqsplit, the right-side arguments are comma-separated and are parsed individually
- /rsnoparse : The command does not evaluate the rightside arg(s).

Any command added without both `/noparse` and `/rsnoparse` is provided with a `/noeval` switch automatically, so if you `@command/add` foo, then foo's arguments are parsed by default, but you can call foo/noeval. Note: when you @hook/override foo, its $-command pattern must be able to match "foo/noeval" as well for the switch to actually be used.

Commands added with `@command/add`, like other standard commands, are always case-insensitive. Until it is hooked, an added command answers "This command has not been implemented."

::: seealso
- [@hook]
- [restrict]
- [evaluation order]
:::

## Added command example

### Examples
```sharp
> @create Dining Machine
> &eat dining=$eat *:@remit %L=%n takes a bite of %0.
> @command/add/noparse eat
> @hook/override eat=dining machine,eat
> eat meat loaf
Walker takes a bite of meat loaf.
> eat randword(apple tomato pear)
Walker takes a bite of randword(apple tomato pear)
```

```sharp
> &drink dining=$^drink(/noeval)? (.*)$:@remit %L=%n drinks %2.
> @set dining/drink=regexp
> @command/add drink
> @hook/override drink=dining machine,drink
> drink reverse(tea)
Walker drinks aet.
> drink/noeval reverse(tea)
Walker drinks reverse(tea).
```
