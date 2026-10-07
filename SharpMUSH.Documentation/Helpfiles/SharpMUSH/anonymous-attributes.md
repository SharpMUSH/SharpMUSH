<!-- help-article
{
  "corpus": "help",
  "id": "anonymous-attributes",
  "lookup": "anonymous attributes",
  "aliases": [
    "LAMBDA",
    "#LAMBDA",
    "#APPLY"
  ],
  "sections": [
    {
      "id": "list-mapping-examples",
      "heading": "List mapping examples",
      "lookup": "anonymous attributes list mapping examples"
    },
    {
      "id": "literal-evaluation",
      "heading": "Literal evaluation",
      "lookup": "anonymous attributes literal evaluation"
    },
    {
      "id": "supported-functions",
      "heading": "Supported functions",
      "lookup": "anonymous attributes supported functions",
      "aliases": [
        "LAMBDA FUNCTIONS"
      ]
    }
  ],
  "redirects": {
    "ANONYMOUS2": "anonymous attributes list mapping examples",
    "LAMBDA2": "anonymous attributes list mapping examples",
    "#LAMBDA2": "anonymous attributes list mapping examples",
    "ANONYMOUS3": "anonymous attributes literal evaluation",
    "LAMBDA3": "anonymous attributes literal evaluation",
    "#LAMBDA3": "anonymous attributes literal evaluation",
    "ANONYMOUS4": "anonymous attributes supported functions",
    "LAMBDA4": "anonymous attributes supported functions",
    "#LAMBDA4": "anonymous attributes supported functions"
  }
}
-->
# Anonymous Attributes

In many cases where a function expects a object/attribute pair that refers to an attribute to evaluate, you can use the form

`#lambda/\<code\>`

instead, and the code will be treated as an attribute's body. The code will normally be parsed twice, so special characters should be escaped where needed.

If the #lambda just calls one other function, the form

`#apply[\<number of arguments\>]/<function name>`

can be used instead. If the argument count is left out, it defaults to 1.

These anonymous attributes should be used for short and simple pieces of code. Anything long or complicated should go in an actual attribute, for readability and maintainability.

## List mapping examples

A typical usage of anonymous attributes would be to convert a list of dbrefs to names, as so:

```sharp
  > say map(#lambda/name(\%0), #3 #12 #23)
  You say, "Joe Robert Sally"
```

The following version uses `#apply` instead:

```sharp
  > say map(#apply/name, #3 #12 #23)
```

Because the code is parsed twice, you can actually build parts of it in place, which is very convenient. Consider this implementation of a lattrval function, which is like `lattr()` but it only returns non-empty attributes:

```sharp
  > &lattrval me=filter(#lambda/hasattrval([decompose(before(%0, /))], \%0), lattr(%0))
```

The first time '#lambda/hasattrval(`[decompose(before(%0, /))]`, \%0)' is parsed in a call like 'u(lattrval, #1234)', it is turned into '#lambda/hasattrval(#1234, %0)', thus avoiding the need for a `setq()` or the like to store the top-level %0 for use in a real attribute called by `filter()`. However, this can lead to problems with evaluating un-trusted code. Use `decompose()` where neccessary.

## Literal evaluation

You can also use `lit()` to avoid having the code evaluated twice, if needed. For example, this code, which returns all unlinked exits in a room:

&lunlinked me=filter(lit(#lambda/strmatch(loc(%0), #-1)), lexits(%0))

This approach is useful both for security in making it harder to evaluate a string that shouldn't be, and for making the code look nicer by not having to escape percent signs, brackets, and other special characters. However, it also makes it harder to build the code string on the fly. Use what's most appropriate.

Finally, a multiple argument example of #apply, which requires less escaping than #lambda for cases where you're just calling another function:

```sharp
  > think mix(#apply2/ansi, r g b, foo bar baz)
```

## Supported functions

The following functions support anonymous attributes:

- [FILTER()]
- [FILTER()]
- [fold()]
- [foreach()]
- [MAP()]
- [MAPSQL()]
- [mix()]
- [munge()]
- [NAMELIST()]
- [SORTBY()]
- [SORTKEY()]
- [speak()]
- [STEP()]
