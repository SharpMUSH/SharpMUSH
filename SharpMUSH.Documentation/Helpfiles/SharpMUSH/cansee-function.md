<!-- help-article
{
  "corpus": "help",
  "id": "cansee-function",
  "lookup": "cansee()",
  "aliases": [
    "cansee",
    "chkreality()",
    "chkreality"
  ],
  "sections": []
}
-->
# cansee()

`cansee(<viewer>, <target>)`<br>
`chkreality(<viewer>, <target>)`

`cansee()` returns 1 when `<viewer>` receives a layer `<target>` transmits, or both are the same object, else 0. It compares the layers only, whether or not reality is enabled.

`chkreality()` returns 1 when `<viewer>` perceives `<target>` under [@reality] as it stands: always, while reality is disabled.

Neither checks DARK, locks or where the two are. You must control both objects. A dbref is taken as it stands, so you can ask about objects you cannot perceive.

```sharp
> think cansee(%#,#10) cansee(#10,%#)
0 1
```

Output: `#10` transmits only `ghost`, which you do not receive, while it receives your `normal`.

::: seealso
- [rxlevel()]
- [@reality perception rules]
:::
