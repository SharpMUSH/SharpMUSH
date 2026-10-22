<!-- help-article
{
  "corpus": "help",
  "id": "function-command",
  "lookup": "@function",
  "aliases": [],
  "sections": [
    {"id": "switches", "heading": "Switches, aliases and clones", "lookup": "@function switches", "aliases": ["@function/disable", "@function/enable", "@function/delete", "@function/alias", "@function/clone"]},
    {
      "id": "definition-permissions",
      "heading": "Definition permissions",
      "lookup": "@function definition permissions"
    },
    {"id": "definition-example", "heading": "Definition example", "lookup": "@function definition example"},
    {
      "id": "startup-loading",
      "heading": "Startup loading",
      "lookup": "@function startup loading"
    },
    {
      "id": "named-arguments",
      "heading": "Named arguments",
      "lookup": "@function named arguments"
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
`@function <name>=<obj>, <attrib>[, <min args>, <max args>[, <restrictions>]]`<br>
`@function <function name>=<object>/<attribute>`<br>
`@function/<switch> <function name>`<br>
`@function/restrict <function name>=<restrictions>`<br>
`@function/args <function name>=[<name> ...]`

When used without any arguments, this command lists the global user-defined functions and counts the built-in ones. A player with the `config.admin` permission or the Functions power also sees the object and attribute each function evaluates, its argument count and whether it is enabled; anyone else sees the names.

When used with a function name, it shows whether that function is a built-in or a @function, whether it is enabled, how many arguments it takes, and the names set with `/args`.

Otherwise, this command defines a global function with the name `<function name>`, which evaluates to `<attribute>` on `<object>`.

- [@function switches]: disabling, enabling, deleting, aliasing and cloning functions
- [@function definition permissions]: who may define one, arguments, and restrictions
- [@function definition example]: a u() function made global
- [@function startup loading]: loading functions when the game starts
- [@function named arguments]: naming the arguments with `/args`
- [@function replacing built-in functions]: overriding a built-in function

## Switches, aliases and clones

`@function/<switch> <function name>`<br>
`@function/alias <alias>=<function name>`<br>
`@function/clone <clone>=<function name>`

`<switch>` can be one of:
- /disable, to disable a @function.
- /enable, to re-enable it.
- /delete, to remove a user-defined or cloned function, or allow a built-in function to be overriden by a user-defined one.
- /restrict, to change the restriction flags on an existing function.
- /builtin or /restore, to remove the @function of that name and turn the built-in back on. `@function/restore *` removes every @function not marked with /preserve.
- /preserve, to mark a @function so `@function/restore *` keeps it.

`@function/alias` creates an alias for the @function `<function name>` so that it can also be called as `<alias>`; it cannot alias a built-in or another alias. `@function/clone` creates a new copy of `<function name>`, a built-in or a @function, named `<clone>`, which works the same initially but can be restricted separately.

## Definition permissions

Defining a global function, and every switch that changes one, needs the `config.admin` permission or the Functions power. `<object>` must also be something that the player using the @function command controls.

A function defined using @function works just like any of the normal MUSH functions, from the user's perspective. The functions are executed by the object, with its powers.

Functions defined via @function should follow the format used by UFUN() - %0 is the first argument passed, %1 is the second argument passed, and so forth. `@function/args` also passes them under names of their own; see [@function named arguments]. Optional third and fourth arguments to @function can be used to set a parser-enforced number of arguments for the function. A negative maximum is treated as the same number without its sign.

An optional fifth argument sets restriction flags, the words `@function/restrict` takes (see [restrict]).

`@function <function name>=<object>/<attribute>` is the same as `@function <function name>=<object>, <attribute>`; it takes no more arguments.

## Definition example

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

Global user-defined functions are not automatically loaded when the game is restarted. In order to avoid objects which attempt to use functions that have not been loaded, a @startup containing @function commands should be set on a wizard object with as low a dbref number as possible; God (#1) is suggested for this use.

For example, if you have one object that stores all your global functions, you could set the following command (the object is #100 in the example):
```sharp
@startup #1=@dolist lattr(#100)=@function ##=#100,##
```

And then store each function as an attribute of the same name on object #100.

## Named arguments

`@function/args <function name>=[<name> ...]` passes the function's arguments under these space-separated names as well as %0, %1 and so on. The attribute reads them with %`\<name\>`. With no names, the names are cleared. Redefining the function clears them too, so set them after the @function in the same @startup.

```sharp
&FN`GREET me=Hello, %<who>. You have %<count> new posts.
@function greet=me,FN`GREET
@function/args greet=who count
think greet(Bob,3)
```
Output: `Hello, Bob. You have 3 new posts.`

The last name can take the rest of the arguments, in the shapes the built-in functions' parameter lists use: `items...` gives `items1`, `items2`, ... and `itemscount`; `key...|value...` deals them out in turn; `case...|result... default` also gives `default` what is left over after whole groups; and a bare `...` lets the caller name them in pairs. See [named arguments rest] for an example of each.

## Replacing built-in functions

Normally, built in functions cannot be overriden by @functions. However, if a built-in function is deleted with `@function/delete`, you can then make a @function with the same name. `@function/restore` will delete the @function and turn the built in version back on.

Using @function on an already-added @function will delete the old one and install a new function with none of the settings of the old one kept.

### Example
```sharp
> @function/delete ansi
> &ansi_fun #1234=%1
> @function ansi=#1234, ansi_fun, 2, 2
> @function/restrict ansi=noguest
```

This creates a new version of ansi() that doesn't do any colorization, and that needs two arguments, like the built-in version. It will be restricted to non-guest players.


::: seealso
- [restrict]
- [named arguments]
- [functions]
- [@startup]
- [fn()]
- [valid()]
:::
