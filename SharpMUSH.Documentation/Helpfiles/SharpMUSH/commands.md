<!-- help-article
{
  "corpus": "help",
  "id": "commands",
  "lookup": "$-commands",
  "aliases": [
    "MACROS",
    "USER-DEFINED COMMANDS"
  ],
  "sections": [
    {
      "id": "patterns-and-registers",
      "heading": "Patterns and registers",
      "lookup": "$-commands patterns and registers"
    }
  ],
  "redirects": {
    "$-COMMANDS2": "$-commands patterns and registers",
    "MACROS2": "$-commands patterns and registers",
    "USER-DEFINED2": "$-commands patterns and registers"
  }
}
-->
# $-commands

User-defined commands can be created by setting `$-commands` on players, things, and rooms. Exits are not checked for `$-commands`. To set a `$-command`:

    &`<attribute>` `<object>`=$`<command pattern>`:`<action list>`

Whenever someone in the same room as the object types the command name, the action list is carried out by the object, as long as:

- the person typing the command is not set GAGGED, passes the object's `@lock`/use and `@lock`/command
- the object is not set NO_COMMAND or HALT

`$-commands` can be run by players, things, and exits.

Such attributes can also be `@triggered` as if the $`<command name>`: did not exist.

It is recommended that `<command pattern>` not begin with "@", as many of SharpMUSH's built-in commands start with "@". Conventionally, global `$-commands` are often named with the "+" prefix, and local `$-commands` commonly have a "+" or "." prefix.

## Patterns and registers

Any number of wildcards, * and ?, may be in present in `<command pattern>`. A * matches any number of characters (including none), and ? matches exactly one character. When the action list is executed, the values on the stack in %0-%9 and v(10)-v(29) are the portions of what the user types that match the first 30 *'s or ?'s. You can also match a regular expression rather than wildcards by setting the REGEXP attribute flag on `<attribute>`; see [regexp] for details. When using named regexp captures, the named arguments can be accessed via r(`<name>`, args).

For example, to make a 'wave' command, you could do the following:
```sharp
    > &DO_WAVE me=$wave *: pose waves to %0.
  You could then type:
    > wave Guest
    Rhyanna waves to Guest.
```

If a command would match, but the enactor can't pass the lock, the object may define generic failure behavior by setting the ``COMMAND_LOCK`FAILURE``, ``COMMAND_LOCK`OFAILURE``, and/or ``COMMAND_LOCK`AFAILURE`` attributes. These are triggered on all objects with matching commands and failing locks, but only if no command successfully matched, and take the place of the usual "Huh?" message.

*BE SURE TO `@LOCK`/USE ME==ME IF YOU SET `$-COMMANDS` ON YOURSELF!*


::: seealso
- [GLOBALS]
- [evaluation order]
- [STACK]
- [%]
- [WILDCARDS]
- [LOCKING]
:::
