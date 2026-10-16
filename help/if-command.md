<!-- help-article
{
  "corpus": "help",
  "id": "if-command",
  "lookup": "@if",
  "aliases": [
    "@ifelse",
    "@skip"
  ],
  "sections": [
    {
      "id": "conditional-examples",
      "heading": "Conditional examples",
      "lookup": "@if conditional examples"
    }
  ],
  "redirects": {
    "@if2": "@if conditional examples"
  }
}
-->
# @if

`@if <boolean>=<true>[, <false>]`<br>
`@skip <boolean>=<false>`

If `<boolean>` is true, the action list `<true>` is run, otherwise the action list `<false>` is run. The action list is not queued, it is run immediately, in the same action list as @if.

Output: the output of the last command run. See [command output].

For RhostMUSH compatability, @skip runs the action list `<false>` when `<boolean>` is false, and does nothing for true values.

@ifelse and `@skip/ifelse` are aliases for @if.

::: seealso
- [@break]
- [@switch]
- [IF()]
- [boolean values]
:::

## Conditional examples

```sharp
> @if 1=say Yes, say No
You say, "Yes"
```

```sharp
> @if 0=say Yes, say No
You say, "No"
```

```sharp
> &foo me=$foo *: say Checking... ; @if %0=say Yes, {say No ; say Sorry!}
```

```sharp
> foo 1
You say, "Checking..."
You say, "Yes"
```

```sharp
> foo 0
You say, "Checking..."
You say, "No"
You say, "Sorry!"
```
