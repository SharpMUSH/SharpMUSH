# formdecode()
`formdecode(<string>[, <paramname>[, <osep>]])`

formdecode() is intended for use with the HTTP Handler. See [http] for more.

formdecode() converts form-encoded data, such as HTTP GET paths (after the ?) or the contents of POST with form-urlencoded data. It searches for the parameter named *<paramname>* and returns with its decoded value.

If *<paramname>* is not given, formdecode() returns a list of parameter names.

If there are multiple values, they will be separated by *<osep>* (default %b)

formdecode() requires libcurl (`@http`) to be enabled.

### Examples

```sharp
> &FORMDATA me=name=Joe&hobby=o%2F%60%20singing%20o%2F%60&like=potato&like=cheese
> say formdecode(v(formdata),name)
You say, "Joe"

> say formdecode(v(formdata),hobby)
You say, "o/\` singing o/\`"

> say formdecode(v(formdata),like,^)
You say, "potato^cheese"

> say formdecode(v(formdata),,,)
You say, "name,hobby,like,like"
```

::: seealso
- [FORMQ()]
:::

# formq()
`formq(<string>[, <prefix>])`

formq() decodes form-encoded data (an HTTP query string or a form-urlencoded body) and sets one **Q-register per parameter**, so HTTP handler softcode can read named parameters directly instead of calling [FORMDECODE()] per field. This is a SharpMUSH extension; there is no PennMUSH equivalent.

Each parameter becomes the register *<prefix><NAME>* (default prefix `FORM.`), so `?name=Joe` is readable as *%q<form.name>*. Names are normalized the same way HTTP header registers are (uppercased; anything outside `A-Z 0-9 _ . -` becomes `_`).

Array parameters collapse into one %r-separated register, whichever way the client spells them: repeated names (`like=a&like=b`) and bracket arrays (`like[]=a&like[]=b`) both produce *%q<form.like>* containing `a%rb`, the same convention as duplicate HTTP headers in *%q<hdr.*>*. Bare tokens (`?debug` with no `=`) become registers with an empty value.

formq() returns the space-separated list of normalized parameter names (without the prefix), mirroring *%q<headers>*.

### Examples

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

