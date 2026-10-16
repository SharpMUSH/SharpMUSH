<!-- help-article
{
  "corpus": "help",
  "id": "action-lists",
  "lookup": "action lists",
  "aliases": [],
  "sections": [
    {
      "id": "action-list-examples",
      "heading": "Action list examples",
      "lookup": "action lists action list examples"
    }
  ],
  "redirects": {
    "ACTION2": "action lists action list examples"
  }
}
-->
# Action Lists

An "action list" is simply a list of MUSH commands which are run together, one after the other. Each command in the list is separated by a semicolon. Action lists appear in many places: in user-defined commands, triggered in `@a`-attributes by the MUSH, and even as arguments to other commands, like `@switch` and `@dolist`.

If part of the command (such as the text in an `@emit`, for example) contains a semi-colon, you may need to enclose that part in curly braces {}. You can also nest action lists inside each other by enclosing each action list in braces {}.

Substitution will be performed on the contents of action lists before they are executed.

Separating two commands with `;|` instead of `;` passes what the first one shows you to the second: see [piping].

::: seealso
- [@-ATTRIBUTES]
- [verbs]
- [$-commands]
- [piping]
:::

## Action list examples

Example 1:
```sharp
    > @asuccess Gift=@pemit %#={The box pops open; surprise!} ; @name me=New Toy ; @desc me=A shiny new toy, just for %n!
    > take gift
    The box pops open; surprise!
    > look new toy
    New Toy
    A shiny new toy, just for Cyclonus!
```

Example 2:
```sharp
    > &TEST me=$test:@emit {Testing; testing; one, two.} ; @dolist 1 2 3={think Test %i0, success.}
    > test
    Testing; testing; one, two.
    Test 1, success.
    Test 2, success.
    Test 3, success.
```


::: seealso
- [attributes]
- [%]
- [@asuccess]
- [@dolist]
:::
