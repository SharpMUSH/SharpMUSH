<!-- help-article
{
  "corpus": "help",
  "id": "formq-function",
  "lookup": "formq()",
  "aliases": [],
  "sections": [
    {"id": "examples", "heading": "Examples", "lookup": "formq() examples"}
  ]
}
-->
# formq()

`formq(<string>[, <prefix>])`

formq() decodes form-encoded data (an HTTP query string or a form-urlencoded body) and sets one **Q-register per parameter**, so HTTP handler softcode can read named parameters directly instead of calling [FORMDECODE()] per field. This is a SharpMUSH extension; there is no PennMUSH equivalent.

Each parameter becomes the register *<prefix><NAME>* (default prefix `FORM.`), so `?name=Joe` is readable as *%q<form.name>*. Names are normalized the same way HTTP header registers are (uppercased; anything outside `A-Z 0-9 _ . -` becomes `_`).

Array parameters collapse into one %r-separated register, whichever way the client spells them: repeated names (`like=a&like=b`) and bracket arrays (`like[]=a&like[]=b`) both produce *%q<form.like>* containing `a%rb`, the same convention as duplicate HTTP headers in `%<hdr.*>`. Bare tokens (`?debug` with no `=`) become registers with an empty value.

formq() returns the space-separated list of normalized parameter names (without the prefix), mirroring `%<headers>`.

## Examples

```sharp
> think [setq(n,formq(name=Joe+Smith&like=a&like=b))]%q<n> / %q<form.name> / %q<form.like>
NAME LIKE / Joe Smith / a
b

> think [null(formq(a=1,arg.))]%q<arg.a>
1
```

The default HTTP verb handlers (see [http examples]) call formq() on the query string for you, so route sub-attributes can read *%q<form.*>* immediately.

::: seealso
- [FORMDECODE()]
- [setq()]
:::
