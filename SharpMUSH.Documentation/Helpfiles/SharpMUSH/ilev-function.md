<!-- help-article
{
  "corpus": "help",
  "id": "ilev-function",
  "lookup": "ilev()",
  "aliases": [
    "ITEXT()",
    "INUM()",
    "%i",
    "%i0",
    "ilev"
  ],
  "sections": [
    {
      "id": "nested-iteration-examples",
      "heading": "Nested iteration examples",
      "lookup": "ilev nested iteration examples"
    }
  ],
  "redirects": {
    "ITEXT2": "ilev nested iteration examples"
  }
}
-->
# ilev()

`ilev()`<br>
`itext(\<n\>)`<br>
%i`\<n\>`<br>
`inum(\<n\>)`

These functions return the equivilent of ## (itext) or #@ (inum) for iter() and @dolist, where an `\<n\>`=0 returns to the current iter or @dolist, `\<n\>`=1 refers to the iter()/@dolist which the current iter() or @dolist is nested in, etc. An `\<n\>` of "L" can be used to refer to the outermost iter()/@dolist. %i`\<n\>` is an alias for itext(`\<n\>`), where `\<n\>` can be from 0 to 9 (or "L").

ilev() returns the current nesting depth, or -1 when used outside an iter() or @dolist. Thus, itext(ilev()) will return the outermost ##, equivilent to %iL.

::: seealso
- [iter()]
- [IBREAK()]
- [@dolist]
:::

## Nested iteration examples

Examples:
```sharp
say iter(red blue green, iter(fish shoe, #@:##))
You say, "1:red 1:red 2:blue 2:blue 3:green 3:green"
```

```sharp
> say iter(red blue green, iter(fish shoe, inum(ilev()):[itext(1)]))
You say, "1:red 1:red 2:blue 2:blue 3:green 3:green"
```

```sharp
> say iter(red blue green,iter(fish shoe, inum(0):[itext(0)]))
You say, "1:fish 2:shoe 1:fish 2:shoe 1:fish 2:shoe"
```

```sharp
> say iter(red blue green,iter(fish shoe, %i1:%i0))
You say, "red:fish red:shoe blue:fish blue:shoe green:fish green:shoe"
```

```sharp
> @dolist red blue green=say iter(fish shoe, %i1:%i0)
You say, "red:fish red:shoe"
You say, "blue:fish blue:shoe"
You say, "green:fish green:shoe"
```


::: seealso
- [iter()]
- [@dolist]
:::
