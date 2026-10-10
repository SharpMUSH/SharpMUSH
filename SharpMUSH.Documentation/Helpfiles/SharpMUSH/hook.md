<!-- help-article
{
  "corpus": "help",
  "id": "hook",
  "lookup": "@hook",
  "aliases": [],
  "sections": [
    {
      "id": "evaluation",
      "heading": "Hook evaluation",
      "lookup": "@hook evaluation"
    },
    {
      "id": "inline",
      "heading": "Inline hooks",
      "lookup": "@hook inline"
    },
    {
      "id": "extension",
      "heading": "Extending commands",
      "lookup": "@hook extension"
    },
    {
      "id": "before-after",
      "heading": "Before and after examples",
      "lookup": "@hook before and after"
    },
    {
      "id": "override",
      "heading": "Override example",
      "lookup": "@hook override"
    },
    {
      "id": "registers",
      "heading": "Argument registers",
      "lookup": "@hook registers"
    }
  ],
  "redirects": {
    "@hook2": "@hook evaluation",
    "@hook3": "@hook inline",
    "@hook4": "@hook extension",
    "@hook5": "@hook before and after",
    "@hook6": "@hook override",
    "@hook7": "@hook registers"
  }
}
-->
# @hook

`@hook/<switch> <command>[=<object>[, <attribute>]]`<br>
`@hook/list [<command>]`

@hook makes the command parser evaluate given attributes at certain points in command evaluation. The possible points, indicated by the proper switch:

- @hook/ignore: The attribute is evaluated before the built-in command is run. If it returns a false value, the command is skipped (the input is still matched against softcoded commands)
- @hook/override: The object/attribute is matched for a $-command, and if it matches, it is run instead of the built-in command, but with the precedence of the built-in command (thus overriding not only the built-in command but any local $-commands that might match). If the match fails, normal built-in command processing continues. Note that all locks and flags on the object (HALT, etc.) still apply.
- @hook/override/inline: Same as `@hook/override`, but the resulting matches are run immediately - not queued for later execution!
- @hook/before: The attribute is evaluated before the built-in command is run.
- @hook/after: The attribute is evaluated after the built-in command is run.
- @hook/extend: If an invalid switch is given to the command, attempt to run a matching $-command in `<object>[/<attribute>]` instead of giving an error. Allows extending built-in commands in softcode without having to rewrite the core functionality.
- @hook/extend/inline: As above, but the $-command won't be queued.

## Hook evaluation

In all cases, %# is the dbref of the object doing the command, and all hooks share the same set of q-registers. With `/before` and `/after`, the results of the evaluated attribute is thrown away like it was wrapped in a call of null(). Also, in cases where a command and function do the same thing (e.g., @pemit and pemit()), only the command gets the hooks.

A number of named registers are available in @hooks, accessible via `r(<name>, args)`, containing the arguments passed to the command. The exact registers available depend on the command type and the arguments passed; see [@hook registers] for a description of all possible registers.

Hooks can also be set in the alias.cnf file.

Leaving out the object and attribute clears an existing hook. Wizards can see existing hooks with @command or `@hook/list`.

## Inline hooks

`@hook/override/inline` and `@hook/extend/inline` allow you to write softcoded commands which act exactly like built-in commands - because they're run immediately, instead of being queued, output from the command appears in the right order relative to other commands in the action list. By default, commands hooked with `/inline` have access to the q-registers of the calling action list, and @breaks in the hooked command propagate to the calling action list, allowing you to write your own control structures.

For example, this adds a new command, @qbreak, which works like @break but stops command execution when %q0 contains a true value:
```sharp
> &qbreak #123=$@qbreak: @break %q0=@pemit/silent %#=Stopping.
> @command/add @qbreak
> @hook/override/inline @qbreak=#123, qbreak
```

This behaviour can be altered by adding the following switches to `@hook/inline`:
- /nobreak: @breaks in the hooked command do not stop the calling action list from running
- /localize: q-registers are saved before the hooked command is run, and restored after it completes
- /clearregs: All q-registers are reset before the hooked command is run. Most useful when used with /localize.

`@hook/inplace` is an alias for `@hook/inline/localize/clearregs/nobreak`.

## Extending commands

`@hook/extend` can be used to add new features to a built-in command, via additional switches, without forcing you to also rewrite the existing functionality like `@hook/override` would. For example:
```sharp
> &who`active #123=$who/active*: @nspemit %#=ufun(fun_who, lwho(%#), switch(%0, ?*, stringsecs(%0)))
> &who`staff #123=$who/staff: @nspemit %#=ufun(fun_who, setunion(lwho(%#), lsearch(all, elock, type^player&(flag^wizard|flag^royalty)))
> @hook/extend WHO=#123
```

This leaves the built-in WHO command working as normal, but adds two new switches for filtering the output in different ways.

`@hook/igswitch` is an alias for `@hook/extend`, for Rhost compatability.

## Before and after examples

An example of @hook:
```sharp
> &top_line #3=pemit(%#, What follows is the results of a look)
> &bottom_line #3=pemit(%#, You're done looking.)
> @hook/before look=#3, top_line
> @hook/after look=#3, bottom_line
> look
What follows is the results of a look
Room Zero
You are in Room Zero. It's very dark here.
You're done looking.
```

```sharp
> &cmd.say #3=$say *: @remit %L=if(hasflag(%#,OOC),<OOC>%b)%n says, "%0"
> @hook/override say=#3, cmd.say
> @set me=OOC
> "test
<OOC> Robert says, "test"
```

## Override example

```sharp
> &dance me=$dance:pose sticks his right foot in ; say Do the hokey pokey ; pose sticks his right foot out
> dance
Walker sticks his right foot in
You say, "Do the hokey pokey"
Walker sticks his right foot out
```

```sharp
> &cmd.say #3=$say *:@remit %l=%n declares, "%0"
> @hook/override say=#3,cmd.say
> dance
Walker sticks his right foot in
Walker sticks his right foot out
Walker declares, "Do the hokey pokey"
```

```sharp
> @hook/override/inplace say=#3,cmd.say
> dance
Walker sticks his right foot in
Walker declares, "Do the hokey pokey"
Walker sticks his right foot out
```

## Argument registers

The following named arguments may be available (via %`\<name\>` or `r(<name>,args)`) in your @hook, depending on the command hooked and the arguments given when run; use `registers(,args)` to get the available registers. `/before`, `/after` and `/ignore` hooks can also use the %u substitution to access the entire command string entered.

| Register | Description |
| --- | --- |
| **Always available:** | |
| ARGS | The entire argument string, before evaluation. Always available. |
| LS | If the command doesn't have multiple left-side args, LS is set to the entire left-side arg (before the = for EQSPLIT commands) |
| LSAC | For commands with multiple left-side-args, the number of left-side args given |
| LSAx | The xth left-side-arg, where x is between 1 and LSAC |
| **Available for EQSPLIT commands:** | |
| EQUALS | If the = was given, this is set to "=" |
| RS | For commands without multiple right-side args, this is the entire right-side arg (after the =) |
| RSAC | For commands with multiple right-side-args, the number of right-side args given |
| RSAx | The xth right-side-arg, where x is between 1 and RSAC |
| **Available for SWITCHES commands (such as @lock):** | |
| SWITCHES | The switch string given. (Note: Currently, switches given to normal commands are not available here, but can be accessed via the %u substitution.) |

A wizard can also give the arguments names of their own with @command/args: after `@command/args @atrchown=attribute owner`, a hook on @atrchown reads %`\<attribute\>` and %`\<owner\>` beside LSA1 and LSA2. See [@command].
