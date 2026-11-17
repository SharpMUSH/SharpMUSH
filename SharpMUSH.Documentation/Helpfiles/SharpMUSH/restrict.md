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
      "id": "roles-powers-and-permissions",
      "heading": "Roles, powers and permissions",
      "lookup": "restrict roles"
    },
    {
      "id": "built-in-restrictions",
      "heading": "Changing a built-in's restriction",
      "lookup": "restrict built-in"
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

In the configuration, the `command_restrictions` and `function_restrictions` settings hold `<name> <restriction>` lines (see [restrict imported]).

From the game:<br>
`@command/restrict <command-name>=<restriction> [" <error message>]`<br>
    `@function`/restrict `<function-name>`=`<restriction>`<br>
    `@attribute`/access `<attribute-name>`=`<restriction>`

For commands, if `<error message>` is given, that message is sent to the player who runs it instead of a generic, unhelpful error message.

- [restrict command restriction rules]: the words a restriction is made of
- [restrict roles]: letting the holders of a role, power, flag or permission in
- [restrict built-in]: loosening or replacing what a built-in asks for
- [restrict function only]: words for functions or commands only
- [restrict imported]: the configuration settings and imported games
- [restrict function and command permissions]: a function that stands in for a command

## Command restriction rules

For commands, `<restriction>` should be an `@lock`-style boolexp (though, for backwards compatability, the restrictions below can be used, and will be converted into an `@lock` automatically). For functions, `<restriction>` should be any combination of the phrases below. For attributes, `<restriction>` is a list of attribute flags, or "none" to create a standard attribute with no restrictions (see [attribute flags]).

    god        Command or function is usable only by God.<br>
    wizard     Usable only by wizards.<br>
    admin      Usable only by Wiz/Roy.<br>
    nogagged   Usable only by non-GAGGED objects.<br>
    nofixed    Usable only by non-FIXED objects.<br>
    noguest    Usable only by non-guest `@powered` objects.<br>
    nobody     Nothing can use it. Same as the /disable switch to `@command` or `@function`.<br>
    logname    When a built-in function is used, log its name, and who is using it.<br>
    logargs    When a built-in function is used, log its name and arguments, and who is using it. @command/restrict refuses both.

A `!` in front of one of these words takes it off again, including one the command or function has of its own (see [restrict built-in]).

The words can be mixed with the role, power, flag and permission words in [restrict roles].

## Roles, powers and permissions

These words let the holders of something use a command or function. Holding any one of them is enough.

    role^<role>        Holds the role, such as `role^builder` or a faction's role.<br>
    power^<power>      Has the power, such as `power^Send_OOB`.<br>
    flag^<flag>        Has the flag.<br>
    perm^<permission>  Holds the permission, such as `perm^players.moderate`. A word with a dot in it, such as `chat.admin`, means the same.<br>
    royalty            Functions only: holds `game.royalty`.<br>
    everyone           Drops every one of these words named before it, so none is needed.

A `!` in front of one turns away whoever holds it, even if they hold another. One that does not exist is held by nobody, so a typo locks rather than opens. God passes them all on a function. A command also takes a flag or power on its own, as `Send_OOB`, where `!` takes it back off.

```sharp
> @function/restrict pemit=role^staff power^Can_Spoof
> @command/restrict +ooc=noguest role^approved !role^muted
> think pemit(me,hi)
Output: #-1 PERMISSION DENIED: NEEDS ROLE^STAFF OR POWER^CAN_SPOOF
```

## Changing a built-in's restriction

A built-in function or command comes with its own restriction. `@function/restrict` and `@command/restrict` change it rather than adding a second one, so they can loosen it as well as tighten it, and need `config.admin` for a built-in.

For a function, each `@function/restrict` starts again from the function's own restriction and applies its words in order. A word in [restrict command restriction rules] sets that restriction, and `!` takes it off: `!wizard` opens a wizard-only function. The first role, power, flag or permission word replaces the function's own; later ones add to it. `@function <name>` shows what it needs now under "Needs one of".

For a command, a restriction replaces its own lock, and `!` takes off one of its own `god`, `noguest`, `nogagged` or `noapp`.

`sound()` comes needing `game.wizard` or `game.send_oob` (the Send_OOB power):
```sharp
> think sound(door.wav)
Output: #-1 PERMISSION DENIED: NEEDS GAME.WIZARD OR GAME.SEND_OOB
> @function/restrict sound=everyone
> @function/restrict sound=role^builder
> @function/restrict sound=
```

The first lets anyone use it, the second only builders, and the third puts it back as it came. `music()`, `stopsound()`, `image()`, `pane()`, `clearscreen()`, `prefetch()` and `expirelinks()` come needing the same, and `image()` also takes `game.send_image`. `cmdlink()` comes open to everyone.

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

The game keeps its `restrict_command` lines in the `command_restrictions` setting and its `restrict_function` lines in `function_restrictions`; a new game ships both empty (see [compatibility commands]). In `command_restrictions`, `noguest`, `nofixed`, `noplayer`, `nobody`, a flag or power name, the words in [restrict roles], a lock and the `" <message>` suffix all mean what they mean in `restrict.cnf`. `function_restrictions` takes `noguest`, `nogagged`, `nofixed`, `admin`, `wizard`, `god`, `nobody`, `logname`, `logargs`, `deprecated`, `localize`, `nosidefx` and the words in [restrict roles], each with or without a `!`.

An imported game keeps its own: reading a PennMUSH `mush.cnf` follows its `include` lines, as `config_file_startup` does, and carries each `restrict_command` line into `command_restrictions` and each `restrict_function` line into `function_restrictions`, and both are applied at startup, so an imported `restrict_function lstats noguest` answers a guest's `lstats()` with `#-1 PERMISSION DENIED` as PennMUSH does. Any other word (`noparse`, `userfn`) is kept but has no effect, and the import names it. A line that cannot be carried (an include that cannot be read, a restriction with no value, a `restrict_attribute`) is logged by name. A `mush.cnf` uploaded through the portal reads each `include` from the file of that name uploaded beside it, never from the server's disk, and an included file that was not uploaded is logged by name.

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
- [roles]
- [LOCKING]
:::
