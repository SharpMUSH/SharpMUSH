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
    {"id": "nesting", "heading": "Nesting and where ## counts", "lookup": "iter nesting"},
    {
      "id": "iteration-examples",
      "heading": "Iteration examples",
      "lookup": "iter iteration examples"
    },
    {"id": "separator-examples", "heading": "Separator and safety examples", "lookup": "iter separator examples"}
  ],
  "redirects": {
    "ITER2": "iter iteration examples"
  }
}
-->
# iter()

`iter(<list>, <pattern>[, <delimiter>[, <output separator>]])`

For each word in `<list>`, iter() evaluates `<pattern>` once, and returns a list of the results of those evaluations. Words in `<list>` are separated by `<delimiter>`, if given, and spaces if not. Words in the resulting list are separated by the given `<output separator>`, or the input delimiter if no output separator is given.

In `<pattern>`, "##" stands for the current word and "#@" for its position in `<list>`, counting from 1. The word is never evaluated as code, so ## is safe on user input. See [compatibility parser in iter is data not code].

parse() is an alias for iter().

- [iter nesting]: nested iter()s, where ## and #@ count, and `innermost_tokens`
- [iter iteration examples]: patterns, positions, and calling an attribute
- [iter separator examples]: an output separator, and keeping code-looking input as text

::: seealso
- [ilev()]
- [IBREAK()]
- [MAP()]
- [@dolist]
:::

## Nesting and where ## counts

If you nest iter()s, ## and #@ refer to the first/outermost iter(). For the current iter(), use %i0 or itext(0), and inum(0). The ilev() function can be used to get the current iter() nesting level.

"##" and "#@" count only where they are written in the iter() itself. In the code of an attribute the pattern calls, such as u(), they are plain text unless that code runs an iter() of its own. They are also plain text outside any iter() or @dolist, and inside lit() or after \.

With the `innermost_tokens` option on, ## and #@ name the innermost iter() or @dolist that is running, as TinyMUX's do, and they also work in the code of an attribute the pattern calls. See [compatibility config innermost loop tokens].

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

## Separator and safety examples

```sharp
> say iter(lnum(3), %i0, ,%r)
You say, "0
1
2"
```

Both ## and %i0 insert the element as text without evaluating it. Read stored player text with v() or get(), not u():
```sharp
> &DATA`TEXT me=[add(1,1)]
> think iter(v(DATA`TEXT),##)
[add(1,1)]
> think iter(v(DATA`TEXT),%i0)
[add(1,1)]
```
