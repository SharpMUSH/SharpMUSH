<!-- help-article
{
  "corpus": "help",
  "id": "restrict",
  "lookup": "restrict",
  "aliases": [],
  "sections": [
    {
      "id": "command-restriction-rules",
      "heading": "Command restriction rules",
      "lookup": "restrict command restriction rules"
    },
    {
      "id": "function-and-command-only",
      "heading": "Restrictions for functions or commands only",
      "lookup": "restrict function only"
    },
    {
      "id": "imported-restrictions",
      "heading": "Restriction settings and imported games",
      "lookup": "restrict imported"
    },
    {
      "id": "function-and-command-permissions",
      "heading": "Function and command permissions",
      "lookup": "restrict function and command permissions"
    }
  ],
  "redirects": {
    "RESTRICT2": "restrict command restriction rules",
    "RESTRICT3": "restrict function and command permissions"
  }
}
-->
# Restrict

Commands, functions and attributes can have their permission levels controlled in the game's configuration, or by wizards from the game via `@command`, `@function` and `@attribute`.

In the configuration, the `command_restrictions` and `function_restrictions` settings hold
`<name> <restriction>` lines (see [restrict imported]).

From the game:<br>
`@command/restrict <command-name>=<restriction> [" <error message>]`<br>
    `@function`/restrict `<function-name>`=`<restriction>`<br>
    `@attribute`/access `<attribute-name>`=`<restriction>`

For commands, if `<error message>` is given, that message is sent to the player who runs it instead of a generic, unhelpful error message.

## Command restriction rules

For commands, `<restriction>` should be an `@lock`-style boolexp (though, for backwards compatability, the restrictions below can be used, and will be converted into an `@lock` automatically). For functions, `<restriction>` should be any combination of the phrases below. For attributes, `<restriction>` is a list of attribute flags, or "none" to create a standard attribute with no restrictions (see [attribute flags]).

    god        Command or function is usable only by God.<br>
    wizard     Usable only by wizards.<br>
    admin      Usable only by Wiz/Roy.<br>
    nogagged   Usable only by non-GAGGED objects.<br>
    nofixed    Usable only by non-FIXED objects.<br>
    noguest    Usable only by non-guest `@powered` objects.<br>
    nobody     Nothing can use it. Same as the /disable switch to `@command` or `@function`.<br>
    logname, logargs  Not supported: a function ignores them, and @command/restrict refuses them.

## Restrictions for functions or commands only

Functions only:<br>
    noparse    Function arguments are not evaluated. Only applies to `@functions`.<br>
    localize   %q-registers are saved/restored when evaluating, as if the `@function` were wrapped in `localize()`.<br>
    userfn     Function can only be called from within an `@function`.<br>
    nosidefx   Don't allow side-effects for this function. See also the function_side_effects `@config` option.<br>
    deprecated This function should no longer be used. Warns the executor's owner whenever someone uses the function.

Commands only:<br>
     noplayer   Cannot be used by players.<br>
     noapp      Cannot be run by an app acting for a player.<br>
Commands can also give any flag, power or type, to restrict to objects with one of those flags or powers, or of one of those types.

## Restriction settings and imported games

The game keeps its `restrict_command` lines in the `command_restrictions` setting and its
`restrict_function` lines in `function_restrictions`; a new game ships both empty (see
[compatibility commands]). In `command_restrictions`, `noguest`, `nofixed`, `noplayer`, `nobody`, a
flag or power name, a lock and the `" <message>` suffix all mean what they mean in `restrict.cnf`.
`function_restrictions` takes `noguest`, `nogagged`, `nofixed`, `admin`, `wizard`, `god` and
`nobody`.

An imported game keeps its own: reading a PennMUSH `mush.cnf` follows its `include` lines,
as `config_file_startup` does, and carries each `restrict_command` line into `command_restrictions`
and each `restrict_function` line into `function_restrictions`, and both are applied at startup, so
an imported `restrict_function lstats noguest` answers a guest's `lstats()` with
`#-1 PERMISSION DENIED` as PennMUSH does. Only the `restrict_function` words that say who may call
a function are applied (`nobody`, `noguest`, `nogagged`, `nofixed`, `admin`, `wizard`, `god`). A
word that changes how it runs (`nosidefx`, `logargs`, `noparse` and the like), or a `!` word that
clears one of a built-in's own restrictions, is kept but has no effect, and the import names it.
A line that cannot be carried (an include that cannot be read, a restriction with no value,
a `restrict_attribute`) is logged by name, and so is every `include` in a `mush.cnf` uploaded through
the portal, which is not followed because it would name a file on the server.

## Function and command permissions

In cases where there are a function and command that do the same thing (like `pemit()` and `@pemit`), the command's restrictions are also checked when the function is called, so to use `pemit()` you must also be able to use `@pemit`. However, a function's restrictions are not checked when a command is called, to allow disabling side-effect functions.

Some functions (like `name()`) have non-side-effect and side-effect versions depending on how many arguments they're called with. The side-effect version can be disabled while keeping the safe non-side-effect form with the 'nosidefx' restriction. This can also be used to disable pure side-effect functions.


Examples:
```sharp
  Only allow admin to use ansi():
    > @function/restrict ansi=admin
```
```sharp
  Don't let anyone set SUSPECT or GAGGED use @emit.
    > @command/restrict @emit=!flag^suspect&!flag^gagged
```

::: seealso
- [@command]
- [@function]
- [@attribute]
- [LOCKING]
:::
