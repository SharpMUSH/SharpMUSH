<!-- help-article
{
  "corpus": "help",
  "id": "literal-command",
  "lookup": "]",
  "aliases": [],
  "sections": [
    {
      "id": "literal-command-examples",
      "heading": "Literal command examples",
      "lookup": "literal command examples",
      "aliases": ["] literal command examples"]
    }
  ],
  "redirects": {
    "]2": "literal command examples"
  }
}
-->
# ]

"]" is a special prefix which can be used before any command. It instructs the MUSH that it shouldn't evaluate the arguments to the command (similar to the "/noeval" switch available on some commands). For example:

```sharp
> say [add(1,1)]
You say, "2"
```

```sharp
> say \[add(1,1)\]
You say, "[add(1,1)]"
```

```sharp
> ]say [add(1,1)]
You say, "[add(1,1)]"
```

```sharp
> ]"[add(1,1)]
You say, "[add(1,1)]"
```

This can be used to pass unevaluated MUSHcode to softcoded commands without having to escape every special character, or to help objects set attributes to contain unevaluated code.

See [literal command examples] for more examples.


::: seealso
- [LIT()]
- [DECOMPOSE()]
- [ESCAPE()]
- [@command]
- [}]
:::

## Literal command examples

Using ']' with $-commands:

```sharp
> &test Tester=$test *: @pemit %#=I got: %0
```

Normal evaluation:
```sharp
> test My name is %n.
I got: My name is Wiggles.
```

Preventing the user input from being evaluated:
```sharp
> ]test My name is %n.
I got: My name is %n.
```

Preventing evaluation of code inside the $-command:
```sharp
> &test Tester=$test *: ]@pemit %#=I got: %0
> test My name is %n.
I got: %0
```

In the last example, '%0' would evaluate to 'My name is Wiggles.' (because the string entered by the user was evaluated), but the @pemit has been told not to evaluate its arguments.
