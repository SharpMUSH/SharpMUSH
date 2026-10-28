<!-- help-article
{
  "corpus": "help",
  "id": "cosh-function",
  "lookup": "cosh()",
  "aliases": [
    "cosh",
    "sinh()",
    "sinh",
    "tanh()",
    "tanh"
  ],
  "sections": []
}
-->
# cosh(), sinh() and tanh()

`cosh(<number>[, <angle type>])`<br>
`sinh(<number>[, <angle type>])`<br>
`tanh(<number>[, <angle type>])`

These return the hyperbolic cosine, sine and tangent of `<number>`, read in the given angle type, or radians by default. See [angles] for the angle types.

These functions come from RhostMUSH.

```sharp
> think cosh(0) [sinh(0)] [tanh(0)]
1 0 0
> think cosh(1)
1.5430806348152437
```

::: seealso
- [cos()]
- [sin()]
- [tan()]
:::
