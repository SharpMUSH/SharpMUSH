<!-- help-article
{
  "corpus": "help",
  "id": "iter-function",
  "lookup": "iter()",
  "aliases": [
    "PARSE()",
    "iter"
  ],
  "sections": [
    {
      "id": "iteration-examples",
      "heading": "Iteration examples",
      "lookup": "iter iteration examples"
    }
  ],
  "redirects": {
    "ITER2": "iter iteration examples"
  }
}
-->
# iter()

`iter(<list>, <pattern>[, <delimiter>[, <output separator>]])`

For each word in `<list>`, iter() evaluates `<pattern>` once, and returns a list of the results of those evaluations. Words in `<list>` are separated by `<delimiter>`, if given, and spaces if not. Words in the resulting list are separated by the given `<ouput separator>`, or a space if no output separator is given.

Prior to each evaluation, every occurrence of the string "##" in `<pattern>` is replaced with the current word from `<list>`. However, because this replacement occurs before evaluation, it cannot be used well in nested iter()s, and should not be used on user input or untrusted `<list>`s, as the word will be evaluated. Instead, you can use the %iX substitution, or the itext() function. The substitution '%iL' refers to the outermost iter of the current expression, and is intended to replace ##.

The string "#@" will be replaced with the position of the current word in `<list>`. Like "##", the replacement occurs before substitution. Use the inum() function for nested iter()s.

If you nest iter()s, ## and #@ refer to the first/outermost iter(). The ilev() function can be used to get the current iter() nesting level.

parse() is an alias for iter().

::: seealso
- [ilev()]
- [ilev()]
- [ilev()]
- [IBREAK()]
- [MAP()]
- [@dolist]
:::

## Iteration examples

Examples:
```sharp
say iter(This is a test string., strlen(%i0))
You say, "4 2 1 4 7"
```

```sharp
> say iter(lnum(5), mul(add(%i0,#@),2))
You say, "2 6 10 14 18"
```

```sharp
> say iter(lexits(here), name(%i0) (owned by [name(owner(%i0))]))
You say, "South (owned by Claudia) North (owned by Roy)"
```

```sharp
> &STRLEN_FN me=strlen(%0)
> say iter(This is a test string., u(STRLEN_FN, %i0))
You say, "4 2 1 4 7"
```

Since this example just evaluates another attribute for each element of the list, it can be done more efficiently using map():<br>
```sharp
> say map(strlen_fun, This is a test string.)
```

```sharp
> say iter(lnum(3), %i0, ,%r)
You say, "0
1
2"
```

An example of why using ## instead of %i0 can be insecure, and lead to unintended evaluation:<br>
```sharp
> say iter((1\,1),add##)
You say, "2"
> say iter((1\,1),add%i0)
You say, "add(1,1)"
```
