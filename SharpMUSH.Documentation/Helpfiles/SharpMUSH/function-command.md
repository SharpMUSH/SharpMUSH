<!-- help-article
{
  "corpus": "help",
  "id": "function-command",
  "lookup": "@function",
  "aliases": [],
  "sections": [
    {
      "id": "definition-permissions",
      "heading": "Definition permissions",
      "lookup": "@function definition permissions"
    },
    {
      "id": "startup-loading",
      "heading": "Startup loading",
      "lookup": "@function startup loading"
    },
    {
      "id": "replacing-built-in-functions",
      "heading": "Replacing built-in functions",
      "lookup": "@function replacing built-in functions"
    }
  ],
  "redirects": {
    "@function2": "@function definition permissions",
    "@function3": "@function startup loading",
    "@function4": "@function replacing built-in functions"
  }
}
-->
# @function

`@function [<function name>]`<br>
`@function[/preserve] <name>=<obj>, <attrib>[, <min args>, <max args>[, <restrictions>]]`<br>
`@function <function name>=<object>/<attribute>`<br>
`@function/<switch> <function name>`<br>
`@function/restrict[/builtin] <function name>=<restrictions>`<br>
`@function/alias <function name>=<alias>`<br>
`@function/clone <function name>=<clone>`<br>
`@function/args <function name>=[<name> ...]`

When used without any arguments, this command lists all global user-defined functions. For wizards and others with the Functions power, it also lists the dbref number and attribute corresponding to the listed functions.

When used with a function name, it displays some information about how that function is parsed, and how many arguments it takes.

`<switch>` can be one of:
- /disable, to disable a function.
- /enable, to re-enable it.
- /delete, to remove a user-defined or cloned function, or allow a built-in function to be overriden by a user-defined one.
- /restrict, to change the restriction flags on an existing function.

`@function/alias` creates an alias for the built-in function `<function name>` so that it can also be called as `<alias>`. `@function/clone` creates a new copy of `<function name>` named `<clone>`, which works the same initially but can be restricted separately. You cannot alias or clone @functions, or alias cloned functions.

Otherwise, this command defines a global function with the name `<function name>`, which evaluates to `<attribute>` on `<object>`.

## Definition permissions

`<object>` can be anything that the player using the @function command controls (if safer_ufun is enabled) or can examine (if not). `<function name>` must be 30 characters or less.

A function defined using @function works just like any of the normal MUSH functions, from the user's perspective. The functions are executed by the object, with its powers.

Functions defined via @function should follow the format used by UFUN() - %0 is the first argument passed, %1 is the second argument passed, and so forth. `@function/args` names them as well: after `@function/args greet=who count`, a call `greet(Bob, 3)` also passes %`\<who\>` and %`\<count\>`. The last name may end in `...` to take the rest: after `@function/args glue=sep items...`, a call `glue(-,a,b c)` passes %`\<sep\>`, then %`\<items1\>` and %`\<items2\>`, and %`\<itemscount\>` (2). Several such names share the rest out in turn: after `@function/args set=obj key... value...`, a call `set(me,a,1,b,2)` passes %`\<key1\>`, %`\<value1\>`, %`\<key2\>`, %`\<value2\>`, %`\<keycount\>` and %`\<valuecount\>`. A bare `...` at the end lets the caller name the rest, in name/value pairs: after `@function/args greet=obj ...`, a call `greet(me,who,Bob)` passes %`\<obj\>` and %`\<who\>`. An odd number of them, or a name that is empty, a number or one of the fixed names, makes the call return an error. With no names, the arguments are passed by position only. Redefining the function clears its names, so set them after defining it. Optional third and fourth arguments to @function can be used to set a parser-enforced number of arguments for the function. If the maximum arguments is negative, any additional arguments are treated as part of the text of the last argument. Note that this behaviour is deprecated, and will be removed in the near future.

An optional fifth argument will set restriction flags.

The `/preserve` switch, for MUX compability, does the same thing as the 'localize' restriction - treats the attribute that's evaluated as if it were called with ulocal() instead of u().

### Example
```sharp
> &WORD_CONCAT #10=%0 %1
> say u(#10/word_concat, foo, bar)
You say, "foo bar"
```

```sharp
> @function word_concat=#10, word_concat
> say word_concat(foo,bar)
You say, "foo bar"
```

## Startup loading

Global user-defined functions are not automatically loaded when the game is restarted. In order to avoid objects which attempt to use functions that have not been loaded, a @startup containing @function commands should be set on a wizard object with as low a dbref number as possible; God (#1) is suggested for this use. You can also create functions from the alias.cnf file.

For example, if you have one object that stores all your global functions, you could set the following command (the object is #100 in the example):
```sharp
@startup #1=@dolist lattr(#100)=@function ##=#100,##
```

And then store each function as an attribute of the same name on object #100.

## Replacing built-in functions

Normally, built in functions cannot be overriden by @functions. However, if a built-in function is deleted with `@function/delete`, you can then make a @function with the same name. "Deleted" built-ins can still be called through the FN() function, and can have restrictions applied with `@function/restrict/builtin`. `@function/restore` will delete the @function and turn the built in version back on.

Using @function on an already-added @function will delete the old one and install a new function with none of the settings of the old one kept.

### Example
```sharp
> @function/delete ansi
> &ansi_fun #1234=%1
> @function ansi=#1234, ansi_fun, 2, -2, noguest
```

This creates a new version of ansi() that doesn't do any colorization, and that needs two arguments, like the built-in version. It will be restricted to non-guest players.


::: seealso
- [restrict]
- [functions]
- [@startup]
- [fn()]
- [valid()]
:::
